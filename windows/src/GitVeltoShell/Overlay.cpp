// IShellIconOverlayIdentifier: one COM class per badge kind (Explorer asks each handler "is this item yours?").
#include "Common.h"
#include "Classes.h"

OverlayHandler::OverlayHandler(Badge kind) : m_ref(1), m_kind(kind) { DllAddRef(); }
OverlayHandler::~OverlayHandler() { DllRelease(); }

IFACEMETHODIMP OverlayHandler::QueryInterface(REFIID riid, void** ppv) {
    static const QITAB qit[] = {
        QITABENT(OverlayHandler, IShellIconOverlayIdentifier),
        { 0 },
    };
    return QISearch(this, qit, riid, ppv);
}

IFACEMETHODIMP_(ULONG) OverlayHandler::AddRef() { return InterlockedIncrement(&m_ref); }

IFACEMETHODIMP_(ULONG) OverlayHandler::Release() {
    ULONG r = InterlockedDecrement(&m_ref);
    if (r == 0) delete this;
    return r;
}

IFACEMETHODIMP OverlayHandler::IsMemberOf(PCWSTR pwszPath, DWORD dwAttrib) {
    (void)dwAttrib;
    if (!pwszPath || !*pwszPath) return S_FALSE;
    std::wstring path = pwszPath;
    // Only local file system paths ("C:\..."): skip virtual items, UNC shares and drive roots.
    if (path.size() < 4 || path[1] != L':' || path[2] != L'\\') return S_FALSE;
    if (!OverlaysEnabledCached()) return S_FALSE;
    return QueryItem(path) == m_kind ? S_OK : S_FALSE;
}

IFACEMETHODIMP OverlayHandler::GetOverlayInfo(PWSTR pwszIconFile, int cchMax, int* pIndex, DWORD* pdwFlags) {
    std::wstring icon = ModuleDir() + L"\\overlays\\" + BadgeId(m_kind) + L".ico";
    if ((int)icon.size() + 1 > cchMax) return E_FAIL;
    wcscpy_s(pwszIconFile, cchMax, icon.c_str());
    *pIndex = 0;
    *pdwFlags = ISIOI_ICONFILE;
    return S_OK;
}

IFACEMETHODIMP OverlayHandler::GetPriority(int* pPriority) {
    // Conflicts first; all other kinds are mutually exclusive anyway.
    *pPriority = m_kind == Badge::Conflict ? 0 : 1;
    return S_OK;
}

// Reading the registry for every item would be wasteful: refresh the switch once a second.
bool OverlaysEnabledCached() {
    static volatile LONG enabled = 1;
    static volatile ULONGLONG checked = 0;
    ULONGLONG now = GetTickCount64();
    if (now - checked > 1000) {
        enabled = OverlaysEnabled() ? 1 : 0;
        checked = now;
    }
    return enabled != 0;
}
