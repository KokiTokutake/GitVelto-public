#include "Common.h"

const CLSID CLSID_GitVeltoContextMenu = { 0x6e7a9b2c, 0x3f1d, 0x4c8e, { 0x9a, 0x51, 0x7d, 0x2b, 0x0c, 0x4e, 0x8f, 0x10 } };
const CLSID CLSID_GitVeltoOverlay[6] = {
    { 0x6e7a9b2c, 0x3f1d, 0x4c8e, { 0x9a, 0x51, 0x7d, 0x2b, 0x0c, 0x4e, 0x8f, 0x11 } },
    { 0x6e7a9b2c, 0x3f1d, 0x4c8e, { 0x9a, 0x51, 0x7d, 0x2b, 0x0c, 0x4e, 0x8f, 0x12 } },
    { 0x6e7a9b2c, 0x3f1d, 0x4c8e, { 0x9a, 0x51, 0x7d, 0x2b, 0x0c, 0x4e, 0x8f, 0x13 } },
    { 0x6e7a9b2c, 0x3f1d, 0x4c8e, { 0x9a, 0x51, 0x7d, 0x2b, 0x0c, 0x4e, 0x8f, 0x14 } },
    { 0x6e7a9b2c, 0x3f1d, 0x4c8e, { 0x9a, 0x51, 0x7d, 0x2b, 0x0c, 0x4e, 0x8f, 0x15 } },
    { 0x6e7a9b2c, 0x3f1d, 0x4c8e, { 0x9a, 0x51, 0x7d, 0x2b, 0x0c, 0x4e, 0x8f, 0x16 } },
};

const wchar_t* BadgeId(Badge b) {
    switch (b) {
    case Badge::Normal: return L"normal";
    case Badge::Modified: return L"modified";
    case Badge::Added: return L"added";
    case Badge::Conflict: return L"conflict";
    case Badge::Untracked: return L"untracked";
    case Badge::Ignored: return L"ignored";
    default: return L"";
    }
}

Badge ParseBadge(const std::wstring& s) {
    for (int i = 0; i < 6; i++)
        if (s == BadgeId((Badge)i)) return (Badge)i;
    return Badge::None;
}

std::wstring ModuleDir() {
    wchar_t buf[MAX_PATH * 4];
    DWORD n = GetModuleFileNameW(g_hInst, buf, ARRAYSIZE(buf));
    std::wstring p(buf, n);
    auto pos = p.find_last_of(L'\\');
    return pos == std::wstring::npos ? p : p.substr(0, pos);
}

std::wstring TrimSlash(const std::wstring& p) {
    std::wstring s = p;
    while (s.size() > 3 && (s.back() == L'\\' || s.back() == L'/')) s.pop_back();
    return s;
}

std::wstring ParentDir(const std::wstring& p) {
    auto s = TrimSlash(p);
    auto pos = s.find_last_of(L"\\/");
    if (pos == std::wstring::npos) return L"";
    if (pos == 2 && s.size() > 3 && s[1] == L':') return s.substr(0, 3); // "C:\"
    if (pos < 2) return L"";
    return s.substr(0, pos);
}

std::wstring FileNameOf(const std::wstring& p) {
    auto s = TrimSlash(p);
    auto pos = s.find_last_of(L"\\/");
    return pos == std::wstring::npos ? s : s.substr(pos + 1);
}

std::wstring Lower(const std::wstring& s) {
    std::wstring r = s;
    if (!r.empty()) CharLowerBuffW(&r[0], (DWORD)r.size());
    return r;
}

bool IsDirectory(const std::wstring& p) {
    DWORD a = GetFileAttributesW(p.c_str());
    return a != INVALID_FILE_ATTRIBUTES && (a & FILE_ATTRIBUTE_DIRECTORY);
}

bool Exists(const std::wstring& p) { return GetFileAttributesW(p.c_str()) != INVALID_FILE_ATTRIBUTES; }

std::optional<std::wstring> FindRepoRoot(const std::wstring& path) {
    std::wstring cur = TrimSlash(path);
    for (int depth = 0; depth < 128 && !cur.empty(); depth++) {
        if (_wcsicmp(FileNameOf(cur).c_str(), L".git") == 0) return std::nullopt;
        std::wstring dotGit = cur + (cur.back() == L'\\' ? L"" : L"\\") + L".git";
        if (Exists(dotGit)) return cur;
        auto parent = ParentDir(cur);
        if (parent.empty() || _wcsicmp(parent.c_str(), cur.c_str()) == 0) break;
        cur = parent;
    }
    return std::nullopt;
}

static std::string ReadSmallFile(const std::wstring& p) {
    HANDLE h = CreateFileW(p.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, 0, nullptr);
    if (h == INVALID_HANDLE_VALUE) return {};
    char buf[4096];
    DWORD n = 0;
    BOOL ok = ReadFile(h, buf, sizeof(buf) - 1, &n, nullptr);
    CloseHandle(h);
    return ok ? std::string(buf, n) : std::string();
}

std::optional<std::wstring> GitDirOf(const std::wstring& root) {
    std::wstring dotGit = TrimSlash(root) + L"\\.git";
    if (IsDirectory(dotGit)) return dotGit;
    auto s = ReadSmallFile(dotGit);
    if (s.rfind("gitdir: ", 0) != 0) return std::nullopt;
    auto line = s.substr(8);
    while (!line.empty() && (line.back() == '\n' || line.back() == '\r' || line.back() == ' ')) line.pop_back();
    std::wstring p = FromUtf8(line);
    for (auto& c : p) if (c == L'/') c = L'\\';
    if (p.size() > 1 && p[1] == L':') return p;
    return TrimSlash(root) + L"\\" + p;
}

std::optional<std::wstring> BranchName(const std::wstring& root) {
    auto gd = GitDirOf(root);
    if (!gd) return std::nullopt;
    auto s = ReadSmallFile(*gd + L"\\HEAD");
    const std::string prefix = "ref: refs/heads/";
    if (s.rfind(prefix, 0) != 0) return std::nullopt;
    auto b = s.substr(prefix.size());
    while (!b.empty() && (b.back() == '\n' || b.back() == '\r' || b.back() == ' ')) b.pop_back();
    return FromUtf8(b);
}

std::string ToUtf8(const std::wstring& s) {
    if (s.empty()) return {};
    int n = WideCharToMultiByte(CP_UTF8, 0, s.data(), (int)s.size(), nullptr, 0, nullptr, nullptr);
    std::string r(n, '\0');
    WideCharToMultiByte(CP_UTF8, 0, s.data(), (int)s.size(), &r[0], n, nullptr, nullptr);
    return r;
}

std::wstring FromUtf8(const std::string& s) {
    if (s.empty()) return {};
    int n = MultiByteToWideChar(CP_UTF8, 0, s.data(), (int)s.size(), nullptr, 0);
    std::wstring r(n, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, s.data(), (int)s.size(), &r[0], n);
    return r;
}

// ---- settings ----

static std::vector<std::wstring> ReadMultiSz(HKEY k, const wchar_t* name, bool* found) {
    std::vector<std::wstring> out;
    DWORD type = 0, size = 0;
    if (RegQueryValueExW(k, name, nullptr, &type, nullptr, &size) != ERROR_SUCCESS || type != REG_MULTI_SZ) { if (found) *found = false; return out; }
    std::vector<wchar_t> buf(size / sizeof(wchar_t) + 2, L'\0');
    if (RegQueryValueExW(k, name, nullptr, &type, (LPBYTE)buf.data(), &size) != ERROR_SUCCESS) { if (found) *found = false; return out; }
    if (found) *found = true;
    const wchar_t* p = buf.data();
    while (*p) { out.emplace_back(p); p += wcslen(p) + 1; }
    return out;
}

static DWORD ReadDword(HKEY k, const wchar_t* name, DWORD def) {
    DWORD v = 0, size = sizeof(v), type = 0;
    if (RegQueryValueExW(k, name, nullptr, &type, (LPBYTE)&v, &size) != ERROR_SUCCESS || type != REG_DWORD) return def;
    return v;
}

bool MenuConfig::IsTop(const std::wstring& id) const {
    for (auto& t : topLevel) if (t == id) return true;
    return false;
}

bool MenuConfig::IsHidden(const std::wstring& id) const {
    for (auto& t : hidden) if (t == id) return true;
    return false;
}

MenuConfig LoadMenuConfig() {
    MenuConfig c;
    HKEY k;
    if (RegOpenKeyExW(HKEY_CURRENT_USER, L"Software\\GitVelto", 0, KEY_READ, &k) != ERROR_SUCCESS) return c;
    bool found = false;
    auto top = ReadMultiSz(k, L"MenuTopLevel", &found);
    if (found) c.topLevel = top;
    c.hidden = ReadMultiSz(k, L"MenuHidden", nullptr);
    c.showBranch = ReadDword(k, L"MenuShowBranch", 1) != 0;
    RegCloseKey(k);
    return c;
}

bool OverlaysEnabled() {
    HKEY k;
    if (RegOpenKeyExW(HKEY_CURRENT_USER, L"Software\\GitVelto", 0, KEY_READ, &k) != ERROR_SUCCESS) return true;
    bool on = ReadDword(k, L"BadgesEnabled", 1) != 0;
    RegCloseKey(k);
    return on;
}
