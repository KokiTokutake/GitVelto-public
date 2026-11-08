// COM server entry points and self-registration (regsvr32 GitVeltoShell.dll, elevated).
#include "Classes.h"
#include <new>

HINSTANCE g_hInst = nullptr;
volatile LONG g_cDllRef = 0;

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        g_hInst = hModule;
        DisableThreadLibraryCalls(hModule);
    }
    return TRUE;
}

// ---- class factory ----

ClassFactory::ClassFactory(int kind) : m_ref(1), m_kind(kind) { DllAddRef(); }
ClassFactory::~ClassFactory() { DllRelease(); }

IFACEMETHODIMP ClassFactory::QueryInterface(REFIID riid, void** ppv) {
    static const QITAB qit[] = { QITABENT(ClassFactory, IClassFactory), { 0 } };
    return QISearch(this, qit, riid, ppv);
}

IFACEMETHODIMP_(ULONG) ClassFactory::AddRef() { return InterlockedIncrement(&m_ref); }

IFACEMETHODIMP_(ULONG) ClassFactory::Release() {
    ULONG r = InterlockedDecrement(&m_ref);
    if (r == 0) delete this;
    return r;
}

IFACEMETHODIMP ClassFactory::CreateInstance(IUnknown* pUnkOuter, REFIID riid, void** ppv) {
    *ppv = nullptr;
    if (pUnkOuter) return CLASS_E_NOAGGREGATION;
    HRESULT hr;
    if (m_kind == -3) {
        auto* p = new (std::nothrow) ExplorerCommandRoot();
        if (!p) return E_OUTOFMEMORY;
        hr = p->QueryInterface(riid, ppv);
        p->Release();
    } else if (m_kind < 0) {
        auto* p = new (std::nothrow) ContextMenuHandler();
        if (!p) return E_OUTOFMEMORY;
        hr = p->QueryInterface(riid, ppv);
        p->Release();
    } else {
        auto* p = new (std::nothrow) OverlayHandler((Badge)m_kind);
        if (!p) return E_OUTOFMEMORY;
        hr = p->QueryInterface(riid, ppv);
        p->Release();
    }
    return hr;
}

IFACEMETHODIMP ClassFactory::LockServer(BOOL fLock) {
    if (fLock) DllAddRef(); else DllRelease();
    return S_OK;
}

STDAPI DllGetClassObject(REFCLSID rclsid, REFIID riid, void** ppv) {
    *ppv = nullptr;
    int kind = -2;
    if (IsEqualCLSID(rclsid, CLSID_GitVeltoContextMenu)) kind = -1;
    if (IsEqualCLSID(rclsid, CLSID_GitVeltoExplorerCommand)) kind = -3;
    for (int i = 0; i < 6; i++) if (IsEqualCLSID(rclsid, CLSID_GitVeltoOverlay[i])) kind = i;
    if (kind == -2) return CLASS_E_CLASSNOTAVAILABLE;
    auto* f = new (std::nothrow) ClassFactory(kind);
    if (!f) return E_OUTOFMEMORY;
    HRESULT hr = f->QueryInterface(riid, ppv);
    f->Release();
    return hr;
}

STDAPI DllCanUnloadNow() { return g_cDllRef > 0 ? S_FALSE : S_OK; }

// ---- registration ----

static const wchar_t* kOverlayNames[6] = {
    // Explorer uses only the first 15 overlay handlers in alphabetical order. Three leading spaces put GitVelto
    // after cloud-storage handlers that use four, but ahead of those using two (OneDrive) or none.
    L"   GitVelto4Normal",
    L"   GitVelto2Modified",
    L"   GitVelto3Added",
    L"   GitVelto1Conflict",
    L"   GitVelto5Untracked",
    L"   GitVelto6Ignored",
};

static std::wstring GuidString(REFCLSID id) {
    wchar_t buf[64];
    StringFromGUID2(id, buf, ARRAYSIZE(buf));
    return buf;
}

static HRESULT SetValue(HKEY root, const std::wstring& key, const wchar_t* name, const std::wstring& value) {
    HKEY k;
    LONG r = RegCreateKeyExW(root, key.c_str(), 0, nullptr, 0, KEY_WRITE | KEY_WOW64_64KEY, nullptr, &k, nullptr);
    if (r != ERROR_SUCCESS) return HRESULT_FROM_WIN32(r);
    r = RegSetValueExW(k, name, 0, REG_SZ, (const BYTE*)value.c_str(), (DWORD)((value.size() + 1) * sizeof(wchar_t)));
    RegCloseKey(k);
    return HRESULT_FROM_WIN32(r);
}

static void DeleteTree(HKEY root, const std::wstring& key) { RegDeleteTreeW(root, key.c_str()); RegDeleteKeyW(root, key.c_str()); }

static HRESULT RegisterClass(REFCLSID id, const wchar_t* friendly) {
    wchar_t dll[MAX_PATH * 4];
    GetModuleFileNameW(g_hInst, dll, ARRAYSIZE(dll));
    std::wstring key = L"Software\\Classes\\CLSID\\" + GuidString(id);
    HRESULT hr = SetValue(HKEY_LOCAL_MACHINE, key, nullptr, friendly);
    if (SUCCEEDED(hr)) hr = SetValue(HKEY_LOCAL_MACHINE, key + L"\\InprocServer32", nullptr, dll);
    if (SUCCEEDED(hr)) hr = SetValue(HKEY_LOCAL_MACHINE, key + L"\\InprocServer32", L"ThreadingModel", L"Apartment");
    if (SUCCEEDED(hr)) hr = SetValue(HKEY_LOCAL_MACHINE, L"Software\\Microsoft\\Windows\\CurrentVersion\\Shell Extensions\\Approved", GuidString(id).c_str(), friendly);
    return hr;
}

static const wchar_t* kMenuTargets[] = { L"*", L"Directory", L"Directory\\Background", L"Drive" };

STDAPI DllRegisterServer() {
    HRESULT hr = RegisterClass(CLSID_GitVeltoContextMenu, L"GitVelto Context Menu");
    for (auto t : kMenuTargets) {
        if (FAILED(hr)) break;
        hr = SetValue(HKEY_LOCAL_MACHINE, std::wstring(L"Software\\Classes\\") + t + L"\\shellex\\ContextMenuHandlers\\GitVelto", nullptr,
                      GuidString(CLSID_GitVeltoContextMenu));
    }
    for (int i = 0; i < 6 && SUCCEEDED(hr); i++) {
        std::wstring friendly = std::wstring(L"GitVelto Overlay ") + BadgeId((Badge)i);
        hr = RegisterClass(CLSID_GitVeltoOverlay[i], friendly.c_str());
        if (SUCCEEDED(hr))
            hr = SetValue(HKEY_LOCAL_MACHINE, std::wstring(L"Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\ShellIconOverlayIdentifiers\\") + kOverlayNames[i],
                          nullptr, GuidString(CLSID_GitVeltoOverlay[i]));
    }
    if (SUCCEEDED(hr)) SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, nullptr, nullptr);
    return hr;
}

STDAPI DllUnregisterServer() {
    for (auto t : kMenuTargets) DeleteTree(HKEY_LOCAL_MACHINE, std::wstring(L"Software\\Classes\\") + t + L"\\shellex\\ContextMenuHandlers\\GitVelto");
    HKEY approved;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, L"Software\\Microsoft\\Windows\\CurrentVersion\\Shell Extensions\\Approved", 0, KEY_WRITE | KEY_WOW64_64KEY, &approved) == ERROR_SUCCESS) {
        RegDeleteValueW(approved, GuidString(CLSID_GitVeltoContextMenu).c_str());
        for (auto& id : CLSID_GitVeltoOverlay) RegDeleteValueW(approved, GuidString(id).c_str());
        RegCloseKey(approved);
    }
    DeleteTree(HKEY_LOCAL_MACHINE, L"Software\\Classes\\CLSID\\" + GuidString(CLSID_GitVeltoContextMenu));
    for (int i = 0; i < 6; i++) {
        DeleteTree(HKEY_LOCAL_MACHINE, L"Software\\Classes\\CLSID\\" + GuidString(CLSID_GitVeltoOverlay[i]));
        DeleteTree(HKEY_LOCAL_MACHINE, std::wstring(L"Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\ShellIconOverlayIdentifiers\\") + kOverlayNames[i]);
    }
    SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, nullptr, nullptr);
    return S_OK;
}
