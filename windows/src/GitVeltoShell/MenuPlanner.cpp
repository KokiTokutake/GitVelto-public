// Explorer context menu layout. Mirrors GitVelto.Core.MenuPlanner (windows/src/GitVelto.Core/Shared.cs);
// keep both in sync (the C# version is covered by unit tests).
#include "Classes.h"

const ActionInfo g_actions[] = {
    { L"sync", L"Git 同期…" },
    { L"commit", L"Git コミット…" },
    { L"log", L"ログを表示" },
    { L"diff", L"差分" },
    { L"diffPrevious", L"前のバージョンと差分" },
    { L"status", L"変更をチェック…" },
    { L"repoBrowser", L"リポジトリブラウザ" },
    { L"reflog", L"Reflog を表示" },
    { L"refBrowser", L"参照の一覧…" },
    { L"pull", L"プル…" },
    { L"fetch", L"フェッチ…" },
    { L"push", L"プッシュ…" },
    { L"switch", L"切り替え/チェックアウト…" },
    { L"branch", L"ブランチを作成…" },
    { L"tag", L"タグを作成…" },
    { L"merge", L"マージ…" },
    { L"rebase", L"リベース…" },
    { L"stashSave", L"変更を退避 (stash)…" },
    { L"stashPop", L"退避した変更を復元 (stash pop)" },
    { L"stashList", L"退避リスト…" },
    { L"bisectStart", L"二分探索を開始 (bisect)…" },
    { L"bisectGood", L"二分探索: 良い (good)" },
    { L"bisectBad", L"二分探索: 悪い (bad)" },
    { L"bisectSkip", L"二分探索: スキップ" },
    { L"bisectReset", L"二分探索を終了 (reset)" },
    { L"add", L"追加…" },
    { L"revert", L"元に戻す…" },
    { L"resolve", L"競合を解決…" },
    { L"editConflict", L"競合を編集…" },
    { L"delete", L"削除 (git rm)…" },
    { L"deleteKeep", L"削除 (ローカルに残す)…" },
    { L"rename", L"名前の変更 (git mv)…" },
    { L"ignore", L"無視リストに追加…" },
    { L"blame", L"Blame" },
    { L"clean", L"クリーンアップ…" },
    { L"submoduleAdd", L"サブモジュールを追加…" },
    { L"submoduleUpdate", L"サブモジュールを更新…" },
    { L"submoduleSync", L"サブモジュールを同期" },
    { L"createPatch", L"パッチを作成…" },
    { L"applyPatch", L"パッチを適用…" },
    { L"export", L"エクスポート…" },
    { L"remotes", L"リモートの管理…" },
    { L"repoSettings", L"リポジトリの設定…" },
    { L"clone", L"Git クローン…" },
    { L"init", L"Git ここにリポジトリを作成…" },
    { L"terminal", L"ターミナルで開く" },
    { L"settings", L"設定…" },
    { L"glossary", L"Git 用語集" },
    { L"commandHistory", L"実行したコマンドの履歴" },
    { L"help", L"ヘルプ" },
    { L"about", L"GitVelto について" },
};
const int g_actionCount = ARRAYSIZE(g_actions);

int ActionIndex(const wchar_t* id) {
    for (int i = 0; i < g_actionCount; i++)
        if (wcscmp(g_actions[i].id, id) == 0) return i;
    return -1;
}

static const wchar_t* kCompareTwo = L"選択した 2 つのファイルを比較";

std::vector<MenuEntry> PlanMenu(const MenuContext& c, const MenuConfig& cfg) {
    bool unknown = false;
    for (auto s : c.states) if (s == Badge::None) unknown = true;
    auto anyOf = [&](std::initializer_list<Badge> kinds) {
        if (c.anyDir || unknown) return true;
        for (auto s : c.states) for (auto k : kinds) if (s == k) return true;
        return false;
    };
    bool single = c.targetCount == 1;
    bool twoFiles = c.targetCount == 2 && !c.anyDir;
    bool singleFile = single && !c.anyDir;
    Badge first = c.states.empty() ? Badge::None : c.states[0];
    bool tracked = singleFile && first != Badge::None && first != Badge::Untracked && first != Badge::Ignored;
    bool isConflict = singleFile && first == Badge::Conflict;

    std::vector<MenuEntry> out;
    auto add = [&](const wchar_t* id, std::wstring title, bool top, bool sep) {
        out.push_back(MenuEntry{ ActionIndex(id), std::move(title), top, sep });
    };

    if (!c.inRepo) {
        if (twoFiles) add(L"diff", kCompareTwo, true, false);
        add(L"clone", g_actions[ActionIndex(L"clone")].title, true, false);
        add(L"init", g_actions[ActionIndex(L"init")].title, true, false);
        for (auto id : { L"terminal", L"settings", L"glossary", L"help", L"about" })
            add(id, g_actions[ActionIndex(id)].title, false, false);
        return out;
    }

    std::vector<const wchar_t*> plan = {
        L"sync", L"commit", nullptr,
        L"log", L"diff", L"diffPrevious", L"status", L"repoBrowser", L"refBrowser", L"reflog", nullptr,
        L"pull", L"fetch", L"push", nullptr,
        L"switch", L"merge", L"rebase", L"branch", L"tag", nullptr,
        L"stashSave", L"stashPop", L"stashList", nullptr,
    };
    if (c.bisecting) plan.insert(plan.end(), { L"bisectGood", L"bisectBad", L"bisectSkip", L"bisectReset", nullptr });
    else plan.insert(plan.end(), { L"bisectStart", nullptr });
    plan.insert(plan.end(), { L"editConflict", L"resolve", L"add", L"revert", L"delete", L"deleteKeep", L"rename", L"ignore", L"blame", L"clean", nullptr });
    plan.push_back(L"submoduleAdd");
    if (c.hasSubmodules) plan.insert(plan.end(), { L"submoduleUpdate", L"submoduleSync" });
    plan.push_back(nullptr);
    plan.insert(plan.end(), { L"createPatch", L"applyPatch", L"export", nullptr, L"remotes", L"repoSettings", L"terminal", nullptr,
                              L"settings", L"glossary", L"commandHistory", L"help", L"about" });

    auto visible = [&](const std::wstring& a) {
        if (cfg.IsHidden(a)) return false;
        if (a == L"diff") return twoFiles || anyOf({ Badge::Modified, Badge::Added, Badge::Conflict });
        if (a == L"diffPrevious" || a == L"blame") return tracked;
        if (a == L"editConflict") return isConflict;
        if (a == L"resolve") return anyOf({ Badge::Conflict });
        if (a == L"add") return anyOf({ Badge::Untracked, Badge::Ignored });
        if (a == L"revert") return anyOf({ Badge::Modified, Badge::Added, Badge::Conflict });
        if (a == L"delete" || a == L"deleteKeep" || a == L"ignore") return !c.isRoot;
        if (a == L"rename") return single && !c.isRoot;
        return true;
    };

    bool pendingSep = false;
    int subCount = 0;
    for (auto id : plan) {
        if (!id) { pendingSep = true; continue; }
        std::wstring a = id;
        if (!visible(a)) continue;
        std::wstring title = g_actions[ActionIndex(id)].title;
        if (a == L"commit" && cfg.showBranch && c.branch) title = L"Git コミット -> \"" + *c.branch + L"\"…";
        if (a == L"diff" && twoFiles) title = kCompareTwo;
        if (cfg.IsTop(a)) {
            add(id, title, true, false);
        } else {
            add(id, title, false, pendingSep && subCount > 0);
            pendingSep = false;
            subCount++;
        }
    }
    return out;
}
