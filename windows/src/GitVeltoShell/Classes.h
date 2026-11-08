#pragma once
#include "Common.h"

bool OverlaysEnabledCached();

class OverlayHandler : public IShellIconOverlayIdentifier {
public:
    explicit OverlayHandler(Badge kind);
    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override;
    IFACEMETHODIMP_(ULONG) AddRef() override;
    IFACEMETHODIMP_(ULONG) Release() override;
    IFACEMETHODIMP IsMemberOf(PCWSTR pwszPath, DWORD dwAttrib) override;
    IFACEMETHODIMP GetOverlayInfo(PWSTR pwszIconFile, int cchMax, int* pIndex, DWORD* pdwFlags) override;
    IFACEMETHODIMP GetPriority(int* pPriority) override;
private:
    ~OverlayHandler();
    LONG m_ref;
    Badge m_kind;
};

struct MenuEntry {
    int action;            // index into the action table (Actions.cpp)
    std::wstring title;
    bool topLevel;
    bool separatorBefore;
};

class ContextMenuHandler : public IShellExtInit, public IContextMenu {
public:
    ContextMenuHandler();
    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override;
    IFACEMETHODIMP_(ULONG) AddRef() override;
    IFACEMETHODIMP_(ULONG) Release() override;
    // IShellExtInit
    IFACEMETHODIMP Initialize(PCIDLIST_ABSOLUTE pidlFolder, IDataObject* pdtobj, HKEY hkeyProgID) override;
    // IContextMenu
    IFACEMETHODIMP QueryContextMenu(HMENU hmenu, UINT indexMenu, UINT idCmdFirst, UINT idCmdLast, UINT uFlags) override;
    IFACEMETHODIMP InvokeCommand(LPCMINVOKECOMMANDINFO pici) override;
    IFACEMETHODIMP GetCommandString(UINT_PTR idCmd, UINT uType, UINT* pReserved, CHAR* pszName, UINT cchMax) override;
private:
    ~ContextMenuHandler();
    LONG m_ref;
    std::vector<std::wstring> m_paths;
    std::vector<MenuEntry> m_entries;
};

// Windows 11 compact context menu ("GitVelto ▸"), registered by the sparse package.
class ExplorerCommandRoot : public IExplorerCommand {
public:
    ExplorerCommandRoot();
    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override;
    IFACEMETHODIMP_(ULONG) AddRef() override;
    IFACEMETHODIMP_(ULONG) Release() override;
    IFACEMETHODIMP GetTitle(IShellItemArray* items, PWSTR* name) override;
    IFACEMETHODIMP GetIcon(IShellItemArray* items, PWSTR* icon) override;
    IFACEMETHODIMP GetToolTip(IShellItemArray* items, PWSTR* tip) override;
    IFACEMETHODIMP GetCanonicalName(GUID* guid) override;
    IFACEMETHODIMP GetState(IShellItemArray* items, BOOL okToBeSlow, EXPCMDSTATE* state) override;
    IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx* bind) override;
    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override;
    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** e) override;
private:
    ~ExplorerCommandRoot();
    LONG m_ref;
    std::vector<MenuEntry> m_entries;
};

class ClassFactory : public IClassFactory {
public:
    explicit ClassFactory(int kind); // -1 = context menu, -3 = Windows 11 explorer command, 0..5 = overlay kinds
    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override;
    IFACEMETHODIMP_(ULONG) AddRef() override;
    IFACEMETHODIMP_(ULONG) Release() override;
    IFACEMETHODIMP CreateInstance(IUnknown* pUnkOuter, REFIID riid, void** ppv) override;
    IFACEMETHODIMP LockServer(BOOL fLock) override;
private:
    ~ClassFactory();
    LONG m_ref;
    int m_kind;
};

// ---- menu planning (mirror of GitVelto.Core.MenuPlanner) ----
struct ActionInfo { const wchar_t* id; const wchar_t* title; };
extern const ActionInfo g_actions[];
extern const int g_actionCount;
int ActionIndex(const wchar_t* id);

struct MenuContext {
    bool inRepo = false;
    int targetCount = 0;
    bool anyDir = false;
    std::vector<Badge> states;   // Badge::None = unknown
    bool isRoot = false;
    bool bisecting = false;
    bool hasSubmodules = false;
    std::optional<std::wstring> branch;
};

std::vector<MenuEntry> PlanMenu(const MenuContext& c, const MenuConfig& cfg);
// Menu entries for an Explorer selection (empty = show nothing).
std::vector<MenuEntry> PlanForPaths(const std::vector<std::wstring>& paths);
// Starts GitVelto.exe <action> with the paths (via a temporary path list file).
HRESULT LaunchAction(const wchar_t* action, const std::vector<std::wstring>& paths, HWND owner);
