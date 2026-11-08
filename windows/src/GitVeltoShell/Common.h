// GitVeltoShell: Explorer shell extension of GitVelto (icon overlays + context menu).
// Plain Win32/COM, no ATL. Talks to GitVeltoCache.exe over a named pipe and starts GitVelto.exe for commands.
#pragma once

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <shellapi.h>
#include <string>
#include <vector>
#include <map>
#include <optional>

extern HINSTANCE g_hInst;
extern volatile LONG g_cDllRef;

inline void DllAddRef() { InterlockedIncrement(&g_cDllRef); }
inline void DllRelease() { InterlockedDecrement(&g_cDllRef); }

// CLSIDs (keep in sync with windows/src/GitVelto/Views/SettingsView.cs and the registration script)
// {6E7A9B2C-3F1D-4C8E-9A51-7D2B0C4E8F10} context menu
// {6E7A9B2C-3F1D-4C8E-9A51-7D2B0C4E8F11..16} overlays: normal, modified, added, conflict, untracked, ignored
extern const CLSID CLSID_GitVeltoContextMenu;
extern const CLSID CLSID_GitVeltoOverlay[6];

enum class Badge : int { None = -1, Normal = 0, Modified = 1, Added = 2, Conflict = 3, Untracked = 4, Ignored = 5 };

const wchar_t* BadgeId(Badge b);
Badge ParseBadge(const std::wstring& s);

// ---- paths / files ----
std::wstring ModuleDir();                                   // folder of GitVeltoShell.dll (= install folder)
std::wstring TrimSlash(const std::wstring& p);              // no trailing backslash (except "C:\")
std::wstring ParentDir(const std::wstring& p);              // "" for roots
std::wstring FileNameOf(const std::wstring& p);
std::wstring Lower(const std::wstring& s);
bool IsDirectory(const std::wstring& p);
bool Exists(const std::wstring& p);
std::optional<std::wstring> FindRepoRoot(const std::wstring& path);
std::optional<std::wstring> GitDirOf(const std::wstring& root);
std::optional<std::wstring> BranchName(const std::wstring& root);
std::string ToUtf8(const std::wstring& s);
std::wstring FromUtf8(const std::string& s);

// ---- settings (HKCU\Software\GitVelto) ----
struct MenuConfig {
    std::vector<std::wstring> topLevel{ L"sync", L"commit" };
    std::vector<std::wstring> hidden;
    bool showBranch = true;
    bool IsTop(const std::wstring& id) const;
    bool IsHidden(const std::wstring& id) const;
};
MenuConfig LoadMenuConfig();
bool OverlaysEnabled();

// ---- cache client ----
struct DirBadges {
    bool ok = false;          // got an answer
    bool pending = false;     // cache still computing
    std::wstring repo;        // repository containing the folder ("" = none)
    std::map<std::wstring, Badge> items; // lower-case child name -> badge
};
// Badges of the children of a folder (in-process cache for a short time).
DirBadges QueryDir(const std::wstring& dir);
// Badge of one item, or Badge::None.
Badge QueryItem(const std::wstring& path);
// Starts GitVeltoCache.exe if it is not running (rate limited).
void EnsureCacheRunning();
