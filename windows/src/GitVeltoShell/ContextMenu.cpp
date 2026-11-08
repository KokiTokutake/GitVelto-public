// IContextMenu: "Git 同期…", "Git コミット…" and the "GitVelto ▸" submenu (TortoiseGit layout).
#include "Classes.h"
#include <strsafe.h>

ContextMenuHandler::ContextMenuHandler() : m_ref(1) { DllAddRef(); }

ContextMenuHandler::~ContextMenuHandler() {
    if (m_icon) DeleteObject(m_icon);
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

// 32bpp bitmap of the GitVelto icon for the menu (menus draw PARGB bitmaps with alpha).
static HBITMAP IconBitmap() {
    int cx = GetSystemMetrics(SM_CXSMICON), cy = GetSystemMetrics(SM_CYSMICON);
    std::wstring exe = ModuleDir() + L"\\GitVelto.exe";
    HICON icon = nullptr;
    PrivateExtractIconsW(exe.c_str(), 0, cx, cy, &icon, nullptr, 1, 0);
    if (!icon) return nullptr;
    BITMAPINFO bi{};
    bi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    bi.bmiHeader.biWidth = cx;
    bi.bmiHeader.biHeight = -cy;
    bi.bmiHeader.biPlanes = 1;
    bi.bmiHeader.biBitCount = 32;
    bi.bmiHeader.biCompression = BI_RGB;
    void* bits = nullptr;
    HDC dc = CreateCompatibleDC(nullptr);
    HBITMAP bmp = CreateDIBSection(dc, &bi, DIB_RGB_COLORS, &bits, nullptr, 0);
    if (bmp) {
        HGDIOBJ old = SelectObject(dc, bmp);
        DrawIconEx(dc, 0, 0, icon, cx, cy, 0, nullptr, DI_NORMAL);
        SelectObject(dc, old);
    }
    DeleteDC(dc);
    DestroyIcon(icon);
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
    if (!m_icon) m_icon = IconBitmap();

    UINT pos = indexMenu;
    UINT maxId = 0;
    auto addItem = [&](HMENU menu, UINT& at, size_t index, bool withIcon) {
        UINT id = idCmdFirst + (UINT)index;
        if (id > idCmdLast) return;
        MENUITEMINFOW mii{ sizeof(mii) };
        mii.fMask = MIIM_STRING | MIIM_ID | MIIM_FTYPE;
        mii.fType = MFT_STRING;
        mii.wID = id;
        mii.dwTypeData = const_cast<wchar_t*>(m_entries[index].title.c_str());
        if (withIcon && m_icon) { mii.fMask |= MIIM_BITMAP; mii.hbmpItem = m_icon; }
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
            addItem(hmenu, pos, i, true);
        } else {
            if (m_entries[i].separatorBefore) addSeparator(sub, subPos);
            addItem(sub, subPos, i, false);
        }
    }
    if (subPos > 0) {
        MENUITEMINFOW mii{ sizeof(mii) };
        mii.fMask = MIIM_STRING | MIIM_SUBMENU | MIIM_FTYPE;
        mii.fType = MFT_STRING;
        mii.hSubMenu = sub;
        mii.dwTypeData = const_cast<wchar_t*>(L"GitVelto");
        if (m_icon) { mii.fMask |= MIIM_BITMAP; mii.hbmpItem = m_icon; }
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
