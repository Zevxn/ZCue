param([switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$buildDirectory = Join-Path $repoRoot 'bin/ReferenceValidation'
$checkDirectory = Join-Path $repoRoot 'bin/PromptReferenceChecks'

if (-not $SkipBuild) {
    dotnet build (Join-Path $repoRoot 'ZCue.sln') --no-restore "-p:OutputPath=$buildDirectory/"
    if ($LASTEXITCODE -ne 0) { throw 'ZCue build failed.' }
}

New-Item -ItemType Directory -Path $checkDirectory -Force | Out-Null
$references = Get-ChildItem -LiteralPath $buildDirectory -Filter '*.dll' | ForEach-Object {
    '<Reference Include="{0}"><HintPath>{1}</HintPath></Reference>' -f $_.BaseName,
        [System.Security.SecurityElement]::Escape($_.FullName)
}
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF><UseWindowsForms>true</UseWindowsForms>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>$($references -join "`n")</ItemGroup>
  <ItemGroup><Resource Include="$([System.Security.SecurityElement]::Escape((Join-Path $repoRoot 'assets/logo.png')))"><Link>assets/logo.png</Link></Resource></ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $checkDirectory 'ReferenceChecks.csproj') -Encoding utf8
'<configuration><packageSources><clear /></packageSources></configuration>' |
    Set-Content -LiteralPath (Join-Path $checkDirectory 'NuGet.Config') -Encoding utf8

@'
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ZCue.Infrastructure;
using ZCue.Models;
using ZCue.Services;
using ZCue.Views;

internal static class Program
{
    private static int _checks;
    private static PromptItem Item(string id, string content, bool enabled = true) =>
        new() { Id = id, Name = id, Content = content, Enabled = enabled };
    private static string Ref(string id) => "{{prompt:" + id + "}}";
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("Failed: " + name);
        _checks++;
    }

    [STAThread]
    private static void Main()
    {
        // SECTION 解析与边界
        var leaf = Item("leaf", " 公共片段\r\n🙂 ", false);
        var middle = Item("middle", Ref("leaf") + "{{date}} {{time}}");
        var root = Item("root", "开始\n" + Ref("middle") + Ref("leaf") + "\n结束");
        var items = new[] { root, middle, leaf };
        var references = new PromptReferenceService(items);
        Check(references.Expand(root).Content == "开始\n" + leaf.Content + "{{date}} {{time}}" + leaf.Content + "\n结束",
            "nested and repeated references preserve Unicode, whitespace and disabled fragments");
        Check(references.Expand(Item("plain", "{{unknown}} $x$ \\path")).Content == "{{unknown}} $x$ \\path",
            "ordinary templates unchanged");
        Check(references.Expand(Item("literal", @"\{{prompt:missing}}")).Content == Ref("missing"), "escaped literal");
        Check(PromptReferenceService.GetReferenceIds(@"\{{prompt:missing}}" + Ref("leaf") + Ref("leaf"))
            .SequenceEqual(new[] { "leaf" }), "dependency parser ignores escaped references and deduplicates");
        var missing = references.Expand(Item("missing", Ref("absent")));
        Check(!missing.Success && missing.Content.Length == 0 && missing.Error!.Contains("absent"), "missing target fails atomically");
        Check(!references.Expand(Item("emptyId", Ref(""))).Success, "empty ID rejected");
        var self = Item("self", Ref("self"));
        Check(!new PromptReferenceService(new[] { self }).Expand(self).Success, "self cycle");
        var a = Item("A", Ref("B"));
        var b = Item("B", Ref("A"));
        var cyclic = new PromptReferenceService(new[] { a, b });
        Check(cyclic.Expand(a).Error!.Contains("A → B → A"), "cycle chain");
        Check(cyclic.GetDependencyIds(new[] { "A" }).SetEquals(new[] { "A", "B" }), "cyclic dependency traversal terminates");
        var diamond = Item("diamond", Ref("middle") + Ref("middle"));
        Check(references.Expand(diamond).Success, "shared dependency is not a cycle");
        var atLimit = Item("limit", new string('x', PromptReferenceService.MaximumContentLength));
        Check(references.Expand(atLimit).Success, "exact length limit");
        atLimit.Content += "x";
        Check(!references.Expand(atLimit).Success, "length overflow");
        var chain = Enumerable.Range(0, 17).Select(i => Item("d" + i, i == 16 ? "end" : Ref("d" + (i + 1)))).ToArray();
        Check(!new PromptReferenceService(chain).Expand(chain[0]).Success, "depth overflow");
        Check(new PromptReferenceService(chain).Expand(chain[1]).Success, "exact depth limit");
        var empty = Item("empty", "");
        var many = Item("many", string.Concat(Enumerable.Repeat(Ref("empty"), 4096)));
        Check(!new PromptReferenceService(new[] { many, empty }).Expand(many).Success, "expansion budget bounds empty repetition");
        var closure = references.GetDependencyIds(new[] { "root" });
        Check(closure.SetEquals(new[] { "root", "middle", "leaf" }), "export includes transitive dependencies");
        Check(references.GetAffectedByDeletion(new HashSet<string> { "leaf" }).Select(i => i.Id).ToHashSet()
            .SetEquals(new[] { "root", "middle" }), "delete lists direct and indirect dependents");
        Check(references.GetAffectedByDeletion(new HashSet<string> { "leaf", "middle" }).Select(i => i.Id)
            .SequenceEqual(new[] { "root" }), "batch delete excludes removed dependents");
        // !SECTION 解析与边界

        // SECTION 变量与最新内容
        var variables = new PromptVariableService();
        var batch = variables.ResolveAll(new[] { root, middle }, items);
        Check(batch.All(result => result.Success && !result.Content.Contains("{{date}}") && !result.Content.Contains("{{time}}")),
            "nested environment variables resolve");
        Check(batch[0].Content.Contains(batch[1].Content), "batch uses the same date and time");
        var fresh = Item("root", "新内容");
        Check(variables.Resolve(root, new[] { fresh }).Content == "新内容", "confirmation uses latest root content");
        Check(!variables.Resolve(root, Array.Empty<PromptItem>()).Success, "deleted active candidate cannot insert stale content");
        var resolver = typeof(PromptVariableService).GetMethod("ResolveVariables", BindingFlags.NonPublic | BindingFlags.Static)!;
        PromptContentResult WithClipboard(string template, string clipboard) => (PromptContentResult)resolver.Invoke(null,
            new object[] { new PromptContentResult(template), "2026-10-15", "12:34", clipboard })!;
        Check(WithClipboard("{{clipboard}} {{time}}", "{{date}} " + Ref("leaf")).Content == "{{date}} " + Ref("leaf") + " 12:34",
            "clipboard markers stay literal without touching the system clipboard");
        Check(!WithClipboard("{{clipboard}}{{clipboard}}", new string('x', 60_000)).Success,
            "variable expansion is bounded before large allocation");
        // !SECTION 变量与最新内容

        // SECTION 存储与导入
        var dataDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "TestData", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dataDirectory);
        PromptCatalogService Catalog(string name)
        {
            var path = System.IO.Path.Combine(dataDirectory, name);
            System.IO.File.WriteAllText(path, "{\"allCommands\":[],\"allCategories\":[]}");
            return new PromptCatalogService(new PromptStorageService(path));
        }
        var source = Catalog("source.json");
        source.Add(leaf);
        source.Add(middle);
        source.Add(root);
        var reloaded = new PromptCatalogService(new PromptStorageService(source.DataFilePath));
        Check(new PromptReferenceService(reloaded.GetAllItems()).Expand(root).Content == references.Expand(root).Content,
            "JSON round trip retains references");
        var renamed = leaf.Clone();
        renamed.Name = "重命名公共片段";
        renamed.Content = "最新内容";
        source.Update(renamed);
        Check(variables.Resolve(root, source.GetAllItems()).Content.Contains("最新内容"), "rename and update retain ID references");
        source.IncrementUsage(root.Id);
        Check(source.GetAllItems().Single(i => i.Id == leaf.Id).UsageCount == 0, "only selected root increments usage");
        var exportPath = System.IO.Path.Combine(dataDirectory, "export.json");
        var storage = new PromptStorageService(source.DataFilePath);
        storage.ExportToFile(exportPath, source.GetAllItems().Where(i => closure.Contains(i.Id)), source.GetAllCategories());
        var imported = storage.LoadImportFile(exportPath);
        Check(imported.Prompts.Single(i => i.Id == root.Id).Content == root.Content, "export preserves raw template and IDs");
        Check(!System.IO.File.ReadAllText(exportPath).Contains("pinyin", StringComparison.OrdinalIgnoreCase), "no alias persistence");
        var target = Catalog("target.json");
        target.Add(Item("leaf", "本地内容", false));
        var importResult = target.Import(imported.Prompts, imported.Categories);
        Check(importResult.SkippedPromptCount == 1 && variables.Resolve(root, target.GetAllItems()).Content.Contains("本地内容"),
            "same ID import retains and references local version");
        // !SECTION 存储与导入

        // SECTION WPF 控件加载
        // 仅加载资源和布局，不启动 AppController、Hook 或托盘，也不操作外部输入框。
        var app = new ZCue.App();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.InitializeComponent();
        foreach (var mode in new[] { AppThemeMode.Light, AppThemeMode.Dark })
        {
            AppThemeManager.Apply(mode);
            var editor = new PromptEditorWindow(root, prompts: items);
            editor.Measure(new System.Windows.Size(620, 720));
            editor.Arrange(new Rect(0, 0, 620, 720));
            editor.UpdateLayout();
            var status = (System.Windows.Controls.TextBlock)editor.FindName("ReferenceStatusTextBlock");
            Check(status.Text.Contains("middle") && status.Text.Contains("leaf"), "editor reference names in " + mode);
            var body = (System.Windows.Controls.TextBox)editor.FindName("ContentTextBox");
            body.Select(0, 2);
            typeof(PromptEditorWindow).GetMethod("InsertContentToken", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(editor, new object[] { Ref("leaf") });
            Check(body.Text.StartsWith(Ref("leaf")) && body.CaretIndex == Ref("leaf").Length,
                "token insertion replaces selection in " + mode);
            var picker = new PromptReferenceWindow(items, root.Id);
            var list = (System.Windows.Controls.ListBox)picker.FindName("PromptListBox");
            Check(list.Items.Count == 2, "picker excludes self in " + mode);
            var search = (Wpf.Ui.Controls.TextBox)picker.FindName("SearchTextBox");
            search.Text = "no-such-prompt";
            Check(list.Items.Count == 0, "picker filters in " + mode);
            picker.Close();
            editor.Close();
        }
        // !SECTION WPF 控件加载
        Console.WriteLine($"Passed {_checks} prompt reference checks. No user data, clipboard or external input was modified.");
    }
}
'@ | Set-Content -LiteralPath (Join-Path $checkDirectory 'Program.cs') -Encoding utf8

dotnet run --project (Join-Path $checkDirectory 'ReferenceChecks.csproj')
if ($LASTEXITCODE -ne 0) { throw 'Prompt reference checks failed.' }
