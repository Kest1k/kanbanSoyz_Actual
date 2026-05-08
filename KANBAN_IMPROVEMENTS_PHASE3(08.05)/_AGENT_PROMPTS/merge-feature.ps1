# Мерж текущей фича-ветки в master.
# Вызов из терминала VS Code, сидя на phase3/<что-нибудь>:
#   .\KANBAN_IMPROVEMENTS_PHASE3(08.05)\_AGENT_PROMPTS\merge-feature.ps1
# Или с явным именем ветки:
#   .\merge-feature.ps1 -Branch phase3/05-description-autoexpand

param(
    [string]$Branch = $(git branch --show-current)
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Branch)) {
    Write-Host "ERR: ветка не определена" -ForegroundColor Red
    exit 1
}
if ($Branch -eq "master" -or $Branch -eq "main") {
    Write-Host "ERR: уже на $Branch — нечего мержить" -ForegroundColor Red
    exit 1
}

$status = git status --porcelain
if ($status) {
    Write-Host "ERR: незакоммиченные изменения, разберись сначала:" -ForegroundColor Red
    Write-Host $status
    exit 1
}

Write-Host ">>> мержу $Branch -> master" -ForegroundColor Cyan
git checkout master
if ($LASTEXITCODE -ne 0) { exit 1 }

git merge --no-ff $Branch -m "merge $Branch"
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERR: конфликт мержа. Резолви руками, потом git commit" -ForegroundColor Red
    exit 1
}

git branch -d $Branch
if ($LASTEXITCODE -ne 0) {
    Write-Host "WARN: не удалось удалить ветку (возможно есть незамерженные коммиты). Удали руками: git branch -D $Branch" -ForegroundColor Yellow
}

Write-Host ">>> готово. master:" -ForegroundColor Green
git log --oneline -5
