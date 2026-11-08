// Windows 11 context menu (the compact first-level menu): IExplorerCommand registered through the sparse package
// (windows/package/AppxManifest.xml). Shows "GitVelto ▸" with the same entries as the classic menu.
#include "Classes.h"
#include <new>
#include <memory>

namespace {

std::vector<std::wstring> PathsOf(IShellItemArray* items) {
    std::vector<std::wstring> paths;
    DWORD n = 0;
    if (!items || FAILED(items->GetCount(&n))) return paths;
    for (DWORD i = 0; i < n && i < 10000; i++) {
        IShellItem* item = nullptr;
        if (FAILED(items->GetItemAt(i, &item))) continue;
        PWSTR p = nullptr;
        if (SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &p)) && p) {
            paths.push_back(TrimSlash(p));
            CoTaskMemFree(p);
        }
        item->Release();
    }
    return paths;
}

HRESULT Dup(const std::wstring& s, PWSTR* out) { return SHStrDupW(s.c_str(), out); }

// One entry of the submenu (an action or a separator).
class SubCommand : public IExplorerCommand {
public:
    SubCommand(int action, std::wstring title, bool separator) : m_ref(1), m_action(action), m_title(std::move(title)), m_separator(separator) { DllAddRef(); }

    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override {
        static const QITAB qit[] = { QITABENT(SubCommand, IExplorerCommand), { 0 } };
        return QISearch(this, qit, riid, ppv);
    }
    IFACEMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&m_ref); }
    IFACEMETHODIMP_(ULONG) Release() override {
        ULONG r = InterlockedDecrement(&m_ref);
        if (r == 0) delete this;
        return r;
    }

    IFACEMETHODIMP GetTitle(IShellItemArray*, PWSTR* name) override {
        if (m_separator) { *name = nullptr; return S_FALSE; }
        return Dup(m_title, name);
    }
    IFACEMETHODIMP GetIcon(IShellItemArray*, PWSTR* icon) override {
        if (m_separator) { *icon = nullptr; return E_NOTIMPL; }
        return Dup(ModuleDir() + L"\\menuicons\\" + g_actions[m_action].id + L".ico", icon);
    }
    IFACEMETHODIMP GetToolTip(IShellItemArray*, PWSTR* tip) override { *tip = nullptr; return E_NOTIMPL; }
    IFACEMETHODIMP GetCanonicalName(GUID* guid) override { *guid = GUID_NULL; return E_NOTIMPL; }
    IFACEMETHODIMP GetState(IShellItemArray*, BOOL, EXPCMDSTATE* state) override { *state = ECS_ENABLED; return S_OK; }
    IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override {
        if (m_separator) return S_OK;
        return LaunchAction(g_actions[m_action].id, PathsOf(items), nullptr);
    }
    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override { *flags = m_separator ? ECF_ISSEPARATOR : ECF_DEFAULT; return S_OK; }
    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** e) override { *e = nullptr; return E_NOTIMPL; }

private:
    ~SubCommand() { DllRelease(); }
    LONG m_ref;
    int m_action;
    std::wstring m_title;
    bool m_separator;
};

class SubCommandEnum : public IEnumExplorerCommand {
public:
    explicit SubCommandEnum(std::vector<IExplorerCommand*> cmds) : m_ref(1), m_cmds(std::move(cmds)) { DllAddRef(); }

    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override {
        static const QITAB qit[] = { QITABENT(SubCommandEnum, IEnumExplorerCommand), { 0 } };
        return QISearch(this, qit, riid, ppv);
    }
    IFACEMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&m_ref); }
    IFACEMETHODIMP_(ULONG) Release() override {
        ULONG r = InterlockedDecrement(&m_ref);
        if (r == 0) delete this;
        return r;
    }

    IFACEMETHODIMP Next(ULONG celt, IExplorerCommand** out, ULONG* fetched) override {
        ULONG n = 0;
        while (n < celt && m_pos < m_cmds.size()) {
            out[n] = m_cmds[m_pos++];
            out[n]->AddRef();
            n++;
        }
        if (fetched) *fetched = n;
        return n == celt ? S_OK : S_FALSE;
    }
    IFACEMETHODIMP Skip(ULONG celt) override { m_pos = std::min(m_cmds.size(), m_pos + celt); return S_OK; }
    IFACEMETHODIMP Reset() override { m_pos = 0; return S_OK; }
    IFACEMETHODIMP Clone(IEnumExplorerCommand** out) override {
        for (auto* c : m_cmds) c->AddRef();
        auto* e = new (std::nothrow) SubCommandEnum(m_cmds);
        if (!e) { for (auto* c : m_cmds) c->Release(); return E_OUTOFMEMORY; }
        e->m_pos = m_pos;
        *out = e;
        return S_OK;
    }

private:
    ~SubCommandEnum() {
        for (auto* c : m_cmds) c->Release();
        DllRelease();
    }
    LONG m_ref;
    std::vector<IExplorerCommand*> m_cmds;
    size_t m_pos = 0;
};

} // namespace

ExplorerCommandRoot::ExplorerCommandRoot() : m_ref(1) { DllAddRef(); }
ExplorerCommandRoot::~ExplorerCommandRoot() { DllRelease(); }

IFACEMETHODIMP ExplorerCommandRoot::QueryInterface(REFIID riid, void** ppv) {
    static const QITAB qit[] = { QITABENT(ExplorerCommandRoot, IExplorerCommand), { 0 } };
    return QISearch(this, qit, riid, ppv);
}

IFACEMETHODIMP_(ULONG) ExplorerCommandRoot::AddRef() { return InterlockedIncrement(&m_ref); }

IFACEMETHODIMP_(ULONG) ExplorerCommandRoot::Release() {
    ULONG r = InterlockedDecrement(&m_ref);
    if (r == 0) delete this;
    return r;
}

IFACEMETHODIMP ExplorerCommandRoot::GetTitle(IShellItemArray*, PWSTR* name) { return Dup(L"GitVelto", name); }

IFACEMETHODIMP ExplorerCommandRoot::GetIcon(IShellItemArray*, PWSTR* icon) { return Dup(ModuleDir() + L"\\GitVelto.exe,0", icon); }

IFACEMETHODIMP ExplorerCommandRoot::GetToolTip(IShellItemArray*, PWSTR* tip) { *tip = nullptr; return E_NOTIMPL; }

IFACEMETHODIMP ExplorerCommandRoot::GetCanonicalName(GUID* guid) { *guid = CLSID_GitVeltoExplorerCommand; return S_OK; }

IFACEMETHODIMP ExplorerCommandRoot::GetState(IShellItemArray* items, BOOL okToBeSlow, EXPCMDSTATE* state) {
    // Planning asks the status cache (named pipe) and reads .git: do it on the background call.
    if (!okToBeSlow) return E_PENDING;
    m_entries = PlanForPaths(PathsOf(items));
    *state = m_entries.empty() ? ECS_HIDDEN : ECS_ENABLED;
    return S_OK;
}

IFACEMETHODIMP ExplorerCommandRoot::Invoke(IShellItemArray*, IBindCtx*) { return E_NOTIMPL; }

IFACEMETHODIMP ExplorerCommandRoot::GetFlags(EXPCMDFLAGS* flags) { *flags = ECF_HASSUBCOMMANDS; return S_OK; }

IFACEMETHODIMP ExplorerCommandRoot::EnumSubCommands(IEnumExplorerCommand** out) {
    *out = nullptr;
    // The compact menu has room for one entry: put the top-level actions (Sync / Commit) first in the submenu.
    std::vector<IExplorerCommand*> cmds;
    auto add = [&](const MenuEntry& e) { cmds.push_back(new (std::nothrow) SubCommand(e.action, e.title, false)); };
    auto sep = [&]() { cmds.push_back(new (std::nothrow) SubCommand(0, L"", true)); };
    bool anyTop = false;
    for (auto& e : m_entries) if (e.topLevel) { add(e); anyTop = true; }
    bool first = true;
    for (auto& e : m_entries) {
        if (e.topLevel) continue;
        if ((first && anyTop) || (!first && e.separatorBefore)) sep();
        add(e);
        first = false;
    }
    for (auto* c : cmds) if (!c) { for (auto* d : cmds) if (d) d->Release(); return E_OUTOFMEMORY; }
    auto* e = new (std::nothrow) SubCommandEnum(std::move(cmds));
    if (!e) return E_OUTOFMEMORY;
    *out = e;
    return S_OK;
}
