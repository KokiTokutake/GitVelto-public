// IContextMenu: "Git 同期…", "Git コミット…" and the "GitVelto ▸" submenu (TortoiseGit layout).
#include "Classes.h"
#include <strsafe.h>

ContextMenuHandler::ContextMenuHandler() : m_ref(1) { DllAddRef(); }

ContextMenuHandler::~ContextMenuHandler() {
    DllRelease();
}

IFACEMETHODIMP ContextMenuHandler::QueryInterface(REFIID riid, void** ppv) {
    static const QITAB qit[] = {
        QITABENT(ContextMenuHandler, IShellExtInit),
        QITABENT(ContextMenuHandler, IContextMenu),
        { 0 },
    };
    return QISearch(this, qit, riid, ppv);
}

IFACEMETHODIMP_(ULONG) ContextMenuHandler::AddRef() { return InterlockedIncrement(&m_ref); }

IFACEMETHODIMP_(ULONG) ContextMenuHandler::Release() {
    ULONG r = InterlockedDecrement(&m_ref);
    if (r == 0) delete this;
    return r;
}

IFACEMETHODIMP ContextMenuHandler::Initialize(PCIDLIST_ABSOLUTE pidlFolder, IDataObject* pdtobj, HKEY) {
    m_paths.clear();
    if (pdtobj) {
        FORMATETC fe = { CF_HDROP, nullptr, DVASPECT_CONTENT, -1, TYMED_HGLOBAL };
        STGMEDIUM stm{};
        if (SUCCEEDED(pdtobj->GetData(&fe, &stm))) {
            HDROP drop = (HDROP)GlobalLock(stm.hGlobal);
            if (drop) {
                UINT n = DragQueryFileW(drop, 0xFFFFFFFF, nullptr, 0);
                for (UINT i = 0; i < n && i < 10000; i++) {
                    UINT len = DragQueryFileW(drop, i, nullptr, 0);
                    std::wstring p(len + 1, L'\0');
                    DragQueryFileW(drop, i, &p[0], len + 1);
                    p.resize(len);
                    if (!p.empty()) m_paths.push_back(TrimSlash(p));
                }
                GlobalUnlock(stm.hGlobal);
            }
            ReleaseStgMedium(&stm);
        }
    } else if (pidlFolder) {
        wchar_t path[MAX_PATH * 4];
        if (SHGetPathFromIDListEx(pidlFolder, path, ARRAYSIZE(path), GPFIDL_DEFAULT)) m_paths.push_back(TrimSlash(path));
    }
    return m_paths.empty() ? E_FAIL : S_OK;
}

// Menus draw 32bpp premultiplied-alpha (PARGB) bitmaps. Converts an icon into one.
static HBITMAP IconToBitmap(HICON icon, int cx, int cy) {
    ICONINFO ii{};
    if (!GetIconInfo(icon, &ii)) return nullptr;
    BITMAPINFO bi{};
    bi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    bi.bmiHeader.biWidth = cx;
    bi.bmiHeader.biHeight = -cy;
    bi.bmiHeader.biPlanes = 1;
    bi.bmiHeader.biBitCount = 32;
    bi.bmiHeader.biCompression = BI_RGB;
    std::vector<DWORD> px((size_t)cx * cy, 0);
    HDC dc = GetDC(nullptr);
    bool ok = ii.hbmColor && GetDIBits(dc, ii.hbmColor, 0, cy, px.data(), &bi, DIB_RGB_COLORS) == cy;
    ReleaseDC(nullptr, dc);
    if (ii.hbmColor) DeleteObject(ii.hbmColor);
    if (ii.hbmMask) DeleteObject(ii.hbmMask);
    if (!ok) return nullptr;
    for (auto& p : px) {
        DWORD a = p >> 24;
        DWORD r = ((p >> 16) & 0xFF) * a / 255, g = ((p >> 8) & 0xFF) * a / 255, b = (p & 0xFF) * a / 255;
        p = (a << 24) | (r << 16) | (g << 8) | b;
    }
    void* bits = nullptr;
    HBITMAP bmp = CreateDIBSection(nullptr, &bi, DIB_RGB_COLORS, &bits, nullptr, 0);
    if (bmp && bits) memcpy(bits, px.data(), px.size() * sizeof(DWORD));
    return bmp;
}

// Bitmaps live for the life of the DLL (shared by all menus; a few dozen small bitmaps).
static SRWLOCK g_iconLock = SRWLOCK_INIT;
static std::map<std::wstring, HBITMAP>* g_icons = nullptr;

// Icon of an action (install folder\menuicons\<id>.ico) or, for "app", the GitVelto icon.
static HBITMAP MenuIcon(const std::wstring& id) {
    int cx = GetSystemMetrics(SM_CXSMICON), cy = GetSystemMetrics(SM_CYSMICON);
    std::wstring key = id + L"@" + std::to_wstring(cx);
    AcquireSRWLockExclusive(&g_iconLock);
    if (!g_icons) g_icons = new std::map<std::wstring, HBITMAP>();
    auto it = g_icons->find(key);
    if (it != g_icons->end()) { HBITMAP b = it->second; ReleaseSRWLockExclusive(&g_iconLock); return b; }
    HICON icon = nullptr;
    if (id == L"app") {
        PrivateExtractIconsW((ModuleDir() + L"\\GitVelto.exe").c_str(), 0, cx, cy, &icon, nullptr, 1, 0);
    } else {
        icon = (HICON)LoadImageW(nullptr, (ModuleDir() + L"\\menuicons\\" + id + L".ico").c_str(), IMAGE_ICON, cx, cy, LR_LOADFROMFILE);
    }
    HBITMAP bmp = icon ? IconToBitmap(icon, cx, cy) : nullptr;
    if (icon) DestroyIcon(icon);
    (*g_icons)[key] = bmp;
    ReleaseSRWLockExclusive(&g_iconLock);
    return bmp;
}

IFACEMETHODIMP ContextMenuHandler::QueryContextMenu(HMENU hmenu, UINT indexMenu, UINT idCmdFirst, UINT idCmdLast, UINT uFlags) {
    if (uFlags & CMF_DEFAULTONLY) return MAKE_HRESULT(SEVERITY_SUCCESS, FACILITY_NULL, 0);
    if (m_paths.empty()) return MAKE_HRESULT(SEVERITY_SUCCESS, FACILITY_NULL, 0);
    // Local paths only (skip libraries / virtual folders).
    for (auto& p : m_paths)
        if (p.size() < 2 || (p[1] != L':' && p.rfind(L"\\\\", 0) != 0)) return MAKE_HRESULT(SEVERITY_SUCCESS, FACILITY_NULL, 0);

    MenuContext ctx;
    ctx.targetCount = (int)m_paths.size();
    std::vector<std::optional<std::wstring>> roots;
    ctx.inRepo = true;
    for (auto& p : m_paths) {
        if (IsDirectory(p)) ctx.anyDir = true;
        auto r = FindRepoRoot(p);
        if (!r) ctx.inRepo = false;
        roots.push_back(r);
    }
    for (auto& p : m_paths) ctx.states.push_back(ctx.inRepo ? QueryItem(p) : Badge::None);
    ctx.isRoot = m_paths.size() == 1 && roots[0] && _wcsicmp(roots[0]->c_str(), m_paths[0].c_str()) == 0;
    if (ctx.inRepo && roots[0]) {
        auto gd = GitDirOf(*roots[0]);
        ctx.bisecting = gd && Exists(*gd + L"\\BISECT_LOG");
        ctx.hasSubmodules = Exists(*roots[0] + L"\\.gitmodules");
        ctx.branch = BranchName(*roots[0]);
    }
    m_entries = PlanMenu(ctx, LoadMenuConfig());

    UINT pos = indexMenu;
    UINT maxId = 0;
    auto addItem = [&](HMENU menu, UINT& at, size_t index) {
        UINT id = idCmdFirst + (UINT)index;
        if (id > idCmdLast) return;
        MENUITEMINFOW mii{ sizeof(mii) };
        mii.fMask = MIIM_STRING | MIIM_ID | MIIM_FTYPE;
        mii.fType = MFT_STRING;
        mii.wID = id;
        mii.dwTypeData = const_cast<wchar_t*>(m_entries[index].title.c_str());
        if (HBITMAP icon = MenuIcon(g_actions[m_entries[index].action].id)) { mii.fMask |= MIIM_BITMAP; mii.hbmpItem = icon; }
        InsertMenuItemW(menu, at++, TRUE, &mii);
        if (index + 1 > maxId) maxId = (UINT)index + 1;
    };
    auto addSeparator = [&](HMENU menu, UINT& at) {
        MENUITEMINFOW mii{ sizeof(mii) };
        mii.fMask = MIIM_FTYPE;
        mii.fType = MFT_SEPARATOR;
        InsertMenuItemW(menu, at++, TRUE, &mii);
    };

    addSeparator(hmenu, pos);
    HMENU sub = CreatePopupMenu();
    UINT subPos = 0;
    for (size_t i = 0; i < m_entries.size(); i++) {
        if (m_entries[i].topLevel) {
            addItem(hmenu, pos, i);
        } else {
            if (m_entries[i].separatorBefore) addSeparator(sub, subPos);
            addItem(sub, subPos, i);
        }
    }
    if (subPos > 0) {
        MENUITEMINFOW mii{ sizeof(mii) };
        mii.fMask = MIIM_STRING | MIIM_SUBMENU | MIIM_FTYPE;
        mii.fType = MFT_STRING;
        mii.hSubMenu = sub;
        mii.dwTypeData = const_cast<wchar_t*>(L"GitVelto");
        if (HBITMAP icon = MenuIcon(L"app")) { mii.fMask |= MIIM_BITMAP; mii.hbmpItem = icon; }
        InsertMenuItemW(hmenu, pos++, TRUE, &mii);
    } else {
        DestroyMenu(sub);
    }
    addSeparator(hmenu, pos);
    return MAKE_HRESULT(SEVERITY_SUCCESS, FACILITY_NULL, maxId);
}

static std::wstring TempPathFile() {
    wchar_t tmp[MAX_PATH];
    GetTempPathW(MAX_PATH, tmp);
    std::wstring dir = std::wstring(tmp) + L"GitVelto";
    CreateDirectoryW(dir.c_str(), nullptr);
    wchar_t name[64];
    swprintf_s(name, L"\\paths-%lu-%llu.txt", GetCurrentProcessId(), GetTickCount64());
    return dir + name;
}

IFACEMETHODIMP ContextMenuHandler::InvokeCommand(LPCMINVOKECOMMANDINFO pici) {
    if (HIWORD(pici->lpVerb) != 0) return E_INVALIDARG; // string verbs are not supported
    UINT index = LOWORD(pici->lpVerb);
    if (index >= m_entries.size()) return E_INVALIDARG;
    const wchar_t* action = g_actions[m_entries[index].action].id;

    // Paths go through a UTF-8 file: no command line length limit, no quoting issues.
    std::wstring file = TempPathFile();
    std::string content;
    for (auto& p : m_paths) content += ToUtf8(p) + "\n";
    HANDLE h = CreateFileW(file.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_TEMPORARY, nullptr);
    if (h == INVALID_HANDLE_VALUE) return E_FAIL;
    DWORD written = 0;
    WriteFile(h, content.data(), (DWORD)content.size(), &written, nullptr);
    CloseHandle(h);

    std::wstring exe = ModuleDir() + L"\\GitVelto.exe";
    std::wstring cmd = L"\"" + exe + L"\" " + action + L" --pathfile \"" + file + L"\" --deletepathfile";
    std::vector<wchar_t> buf(cmd.begin(), cmd.end());
    buf.push_back(0);
    std::wstring cwd = IsDirectory(m_paths[0]) ? m_paths[0] : ParentDir(m_paths[0]);
    STARTUPINFOW si{ sizeof(si) };
    PROCESS_INFORMATION pi{};
    AllowSetForegroundWindow(ASFW_ANY);
    if (!CreateProcessW(exe.c_str(), buf.data(), nullptr, nullptr, FALSE, 0, nullptr, cwd.empty() ? nullptr : cwd.c_str(), &si, &pi)) {
        std::wstring msg = L"GitVelto.exe を起動できません。\n" + exe;
        MessageBoxW(pici->hwnd, msg.c_str(), L"GitVelto", MB_ICONERROR);
        DeleteFileW(file.c_str());
        return HRESULT_FROM_WIN32(GetLastError());
    }
    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);
    return S_OK;
}

IFACEMETHODIMP ContextMenuHandler::GetCommandString(UINT_PTR idCmd, UINT uType, UINT*, CHAR* pszName, UINT cchMax) {
    if (idCmd >= m_entries.size()) return E_INVALIDARG;
    const ActionInfo& a = g_actions[m_entries[idCmd].action];
    switch (uType) {
    case GCS_VERBW:
        return StringCchCopyW((LPWSTR)pszName, cchMax, (std::wstring(L"GitVelto.") + a.id).c_str());
    case GCS_HELPTEXTW:
        return StringCchCopyW((LPWSTR)pszName, cchMax, m_entries[idCmd].title.c_str());
    case GCS_VALIDATEW:
        return S_OK;
    default:
        return E_NOTIMPL;
    }
}
