// Named-pipe client of GitVeltoCache.exe with a short-lived in-process cache per folder.
#include "Common.h"

namespace {

struct Entry {
    ULONGLONG tick = 0;
    DirBadges data;
};

SRWLOCK g_lock = SRWLOCK_INIT;
std::map<std::wstring, Entry>* g_cache = nullptr; // lower-case folder -> entry (leaked on purpose: lives as long as the DLL)
ULONGLONG g_lastStart = 0;

const ULONGLONG kReadyTtl = 600;    // ms
const ULONGLONG kPendingTtl = 250;  // ms
const ULONGLONG kFailTtl = 2000;    // ms (cache not reachable)

std::wstring PipeName() {
    wchar_t user[256];
    DWORD n = ARRAYSIZE(user);
    if (!GetUserNameW(user, &n)) user[0] = 0;
    return std::wstring(L"\\\\.\\pipe\\GitVeltoCache-") + user;
}

bool WaitIo(HANDLE h, OVERLAPPED& ov, DWORD timeoutMs, DWORD& transferred) {
    if (WaitForSingleObject(ov.hEvent, timeoutMs) != WAIT_OBJECT_0) {
        CancelIo(h);
        GetOverlappedResult(h, &ov, &transferred, TRUE);
        return false;
    }
    return GetOverlappedResult(h, &ov, &transferred, FALSE) != FALSE;
}

// Sends one request line and reads the whole answer (until the server closes the pipe).
bool Request(const std::wstring& line, std::string& answer, DWORD timeoutMs) {
    const std::wstring name = PipeName();
    HANDLE h = INVALID_HANDLE_VALUE;
    for (int attempt = 0; attempt < 2; attempt++) {
        h = CreateFileW(name.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, nullptr);
        if (h != INVALID_HANDLE_VALUE) break;
        DWORD err = GetLastError();
        if (err == ERROR_PIPE_BUSY) { WaitNamedPipeW(name.c_str(), 150); continue; }
        if (err == ERROR_FILE_NOT_FOUND) EnsureCacheRunning();
        return false;
    }
    if (h == INVALID_HANDLE_VALUE) return false;

    OVERLAPPED ov{};
    ov.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    bool ok = false;
    std::string req = ToUtf8(line) + "\n";
    DWORD done = 0;
    ULONGLONG start = GetTickCount64();
    if (WriteFile(h, req.data(), (DWORD)req.size(), &done, &ov) || GetLastError() == ERROR_IO_PENDING) {
        if (WaitIo(h, ov, 500, done)) {
            char buf[8192];
            ok = true;
            for (;;) {
                ULONGLONG elapsed = GetTickCount64() - start;
                if (elapsed >= timeoutMs) { ok = false; break; }
                ResetEvent(ov.hEvent);
                DWORD got = 0;
                BOOL r = ReadFile(h, buf, sizeof(buf), &got, &ov);
                if (!r && GetLastError() == ERROR_BROKEN_PIPE) break;
                if (!r && GetLastError() != ERROR_IO_PENDING && GetLastError() != ERROR_MORE_DATA) { ok = false; break; }
                if (!WaitIo(h, ov, (DWORD)(timeoutMs - elapsed), got)) {
                    if (GetLastError() == ERROR_BROKEN_PIPE || GetLastError() == ERROR_HANDLE_EOF) break;
                    if (GetLastError() == ERROR_MORE_DATA) { answer.append(buf, got); continue; }
                    ok = false;
                    break;
                }
                if (got == 0) break;
                answer.append(buf, got);
            }
        }
    }
    CloseHandle(ov.hEvent);
    CloseHandle(h);
    return ok;
}

DirBadges Parse(const std::string& text) {
    DirBadges d;
    d.ok = true;
    size_t pos = 0;
    bool first = true;
    while (pos < text.size()) {
        size_t nl = text.find('\n', pos);
        if (nl == std::string::npos) nl = text.size();
        std::wstring line = FromUtf8(text.substr(pos, nl - pos));
        pos = nl + 1;
        if (!line.empty() && line.back() == L'\r') line.pop_back();
        size_t tab = line.find(L'\t');
        if (first) {
            first = false;
            // repo \t <root> \t ready|pending
            if (line.rfind(L"repo\t", 0) == 0) {
                std::wstring rest = line.substr(5);
                size_t t2 = rest.find(L'\t');
                d.repo = t2 == std::wstring::npos ? rest : rest.substr(0, t2);
                d.pending = t2 != std::wstring::npos && rest.substr(t2 + 1) == L"pending";
                continue;
            }
        }
        if (tab == std::wstring::npos) continue;
        Badge b = ParseBadge(line.substr(tab + 1));
        if (b != Badge::None) d.items[Lower(line.substr(0, tab))] = b;
    }
    return d;
}

} // namespace

void EnsureCacheRunning() {
    ULONGLONG now = GetTickCount64();
    AcquireSRWLockExclusive(&g_lock);
    bool skip = g_lastStart != 0 && now - g_lastStart < 5000;
    if (!skip) g_lastStart = now;
    ReleaseSRWLockExclusive(&g_lock);
    if (skip) return;
    std::wstring exe = ModuleDir() + L"\\GitVeltoCache.exe";
    if (!Exists(exe)) return;
    std::wstring cmd = L"\"" + exe + L"\"";
    STARTUPINFOW si{ sizeof(si) };
    PROCESS_INFORMATION pi{};
    std::vector<wchar_t> buf(cmd.begin(), cmd.end());
    buf.push_back(0);
    if (CreateProcessW(exe.c_str(), buf.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW | DETACHED_PROCESS, nullptr,
                       ModuleDir().c_str(), &si, &pi)) {
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
    }
}

DirBadges QueryDir(const std::wstring& rawDir) {
    std::wstring dir = TrimSlash(rawDir);
    std::wstring key = Lower(dir);
    ULONGLONG now = GetTickCount64();
    AcquireSRWLockShared(&g_lock);
    if (g_cache) {
        auto it = g_cache->find(key);
        if (it != g_cache->end()) {
            const Entry& e = it->second;
            ULONGLONG ttl = !e.data.ok ? kFailTtl : e.data.pending ? kPendingTtl : kReadyTtl;
            if (now - e.tick < ttl) {
                DirBadges copy = e.data;
                ReleaseSRWLockShared(&g_lock);
                return copy;
            }
        }
    }
    ReleaseSRWLockShared(&g_lock);

    DirBadges result;
    std::string answer;
    if (Request(L"dir\t" + dir, answer, 1500)) result = Parse(answer);

    AcquireSRWLockExclusive(&g_lock);
    if (!g_cache) g_cache = new std::map<std::wstring, Entry>();
    if (g_cache->size() > 512) {
        for (auto it = g_cache->begin(); it != g_cache->end();) {
            if (now - it->second.tick > kFailTtl) it = g_cache->erase(it); else ++it;
        }
    }
    (*g_cache)[key] = Entry{ GetTickCount64(), result };
    ReleaseSRWLockExclusive(&g_lock);
    return result;
}

Badge QueryItem(const std::wstring& path) {
    std::wstring p = TrimSlash(path);
    std::wstring parent = ParentDir(p);
    if (parent.empty()) return Badge::None;
    DirBadges d = QueryDir(parent);
    auto it = d.items.find(Lower(FileNameOf(p)));
    return it == d.items.end() ? Badge::None : it->second;
}
