// 说明：本文件是项目文档（阶段 7 报告），不含代码。

# 阶段 7：v2.0.0 —— 中英双语、关于窗口、手动更新检查

阶段 7 的目标（用户 m02091 + m02134）：
把 GUI 与 CLI 做成**中英双语**（首次启动跟随系统、切换立即生效、记住选择），新增**关于**窗口与**手动**更新检查，
同时补上构建卫生（不再把 1 GB 的原生符号文件复制进每个 `bin`），双语 README 与中英截图，最后打 `v2.0.0` 标签跑通 CI。

状态：**已交付**（`v2.0.0` 的 Release 由 CI 产出，见第 6 节）。

---

## 1. 语言判定与文案表

| 关注点 | 实现 | 位置 |
|---|---|---|
| 语言枚举 | `System` / `Chinese` / `English`；`system`、`zh`、`en` 是配置文件里的稳定标识 | `src/RePKG.Shared/Localization/AppLanguage.cs` |
| 文案表 | 一份键 → 中英双值；中英键集合与占位符必须完全一致（有单测守着） | `LocalizationCatalog.cs` + `CatalogApp/CatalogGui/CatalogCli.cs` |
| 查表与格式化 | `ILocalizer.Get/Format`，缺键返回键名（便于暴露漏配） | `Localizer.cs` |
| 系统语言探测 | Windows：`GetUserDefaultUILanguage` 取主语言 ID；类 Unix：`LC_ALL`/`LC_MESSAGES`/`LANG`；都拿不到才看 `CultureInfo` | `SystemLanguage.cs` |
| 语言来源优先级 | `--lang` → `REPKG_LANG` → `settings.json` → 系统 | `LanguageResolver.cs` |
| 设置持久化 | `%APPDATA%\RePKG-Enhanced\settings.json`，`{"version":1,"language":"zh"}`，原子替换写入 | `src/RePKG.Shared/Settings/SettingsStore.cs` |

**为什么不用 `CultureInfo` 判断系统语言**：本项目开了 `InvariantGlobalization=true`（AOT 体积与行为稳定），
在该模式下 `new CultureInfo("zh-CN")` 会直接抛 `CultureNotFoundException`，`CurrentUICulture.Name` 也拿不到真实区域名。
所以系统语言只能问操作系统本身（Windows API / 环境变量），单测里同样不能构造 `CultureInfo("zh-CN")`。

## 2. GUI：切换语言立即生效

- 启动时 `LocalizationResources.Apply(Application, localizer)` 把当前语言的整本文案表挂成 `Application.Resources.MergedDictionaries[0]`。
- 所有 XAML 文案写 `{DynamicResource gui.addPackage}` 这类键；ViewModel 里的状态/汇总文案不再是字面量，
  而是 `SetStatus(key, args...)` 存下键与参数，`RenderStatus()` 再查表——换语言时重放一遍即可。
- 换语言时把字典**摘下 → 清空 → 重灌 → 插回**：Avalonia 的 `DynamicResource` 只在资源字典集合变化时重求值，
  只改内容不会刷新。
- 同一个 `ResourceDictionary` 实例不能挂到两个 `Application` 上（无头测试每题一个 App 会报
  `The ResourceDictionary already has a parent`），所以 `LocalizationResources` 在宿主变化时新建实例。

## 3. 关于窗口与手动更新检查

| 需求 | 实现 |
|---|---|
| 构建版本 / 完整版本号 | `AppInfo.Version` / `AppInfo.InformationalVersion`（`Directory.Build.props` 的 `Version=2.0.0`） |
| 作者 / 仓库 / 许可 | `AppInfo.Author = "dieraun"`、`RepositoryUrl`、`LicenseName = "MIT"` |
| 运行环境 | `AppInfo.RuntimeDescription`（`.NET 10.0.12 · win-x64`） |
| 检查更新 | `UpdateChecker.CheckAsync` → `GET https://api.github.com/repos/dieraun/RePKG-Enhanced/releases/latest` |
| 版本比较 | `SemanticVersion.IsNewer`（逐段数字比较，忽略 `-beta` 之类的后缀） |
| 联网边界 | **只有**点「检查更新」或 `repkg --check-update` 才发请求；启动、加载、导出全程不联网 |

`UpdateChecker` 支持注入 `HttpMessageHandler`（`UpdateChecker.CreateForTesting`），
所以更新检查的测试完全离线：正常应答、403、断网、坏 JSON 都有覆盖。

## 4. CLI

```
repkg --lang en --help          # 英文帮助
repkg extract D:\workshop -r -o out --lang en
repkg --check-update            # 唯一联网的命令
set REPKG_LANG=en               # 环境变量（优先级低于 --lang，高于 settings.json）
```

`--version` 输出 `repkg 2.0.0`；退出码语义不变：`0` 成功、`1` 部分失败、`2` 用法错误、`130` 用户取消。

## 5. 构建卫生（磁盘占用）

之前 `D:\BetterRePKG` 有 3.41 GB，其中 `bin`+`obj` 占 94.5%，最大头是 `SkiaSharp.NativeAssets.Win32` 每个 RID 都带的
`libSkiaSharp.pdb`（约 82 MB）被复制了 23 份 = 1.82 GB，其中 1.12 GB 属于本机永远不会用的 `win-x86`/`win-arm64`。

处理方式：新增 `Directory.Build.targets`，在 `CopyFilesToOutputDirectory` / `ComputeResolvedFilesToPublishList`
之前把 `runtimes\**\native\*.pdb` 从 `ReferenceCopyLocalPaths` 与 `ResolvedFileToPublish` 里摘掉
（托管 pdb 保留，调试体验不变；原生库不需要调试符号）。可用 `-p:ExcludeNativeDebugSymbols=false` 关闭。
实测：重建后一个测试工程的 `bin` 里 >1 MB 的 pdb 数量从 3 个（244 MB）变成 0。

## 6. 验证

| 项目 | 结果 |
|---|---|
| `dotnet build RePKG-Enhanced.slnx -c Release` | 0 error（仅测试项目的 `xUnit1051` 提示） |
| `tests\RePKG.Tests` | 40 passed |
| `tests\RePKG.Tests.Shared` | 113 passed（含本地化、设置、更新检查） |
| `tests\RePKG.Tests.Cli` | 19 passed（含中英帮助、错误、汇总文案） |
| `tests\RePKG.Tests.Gui` | 24 passed（含语言切换、关于窗口、离线更新检查） |
| 大文件门禁（`REPKG_BIG_TESTS=1`） | 2 passed，分配增量仍为 1.0 MiB |
| 截图 | `docs/images/main-window-{zh,en}-{light,dark}.png`、`docs/images/about-{zh,en}.png` |

## 7. 已知限制

1. 语言只有中文和英文；其他系统语言会落到英文。
2. `--check-update` 只报告版本，不自动下载或替换（离线优先，更新仍需手动）。
3. GitHub API 有速率限制（未认证 60 次/小时/IP），失败时只提示原因，不影响其他功能。
4. 语言用 JSON 文件保存，不做多用户隔离（`%APPDATA%` 是按用户的）。
5. 数值/时间格式始终用不变文化输出（`0.1s`、`1.2 MiB`），不随语言变小数点。

## 8. 发布前实测发现的两个运行时问题（已修）

1. **NativeAOT 下 `Assembly.GetEntryAssembly()` 未必是装了内嵌资源的那个程序集。**
   原先 `NativeBootstrap` 只看入口程序集与本程序集；改为 `CollectCandidateAssemblies()`：入口、`GetExecutingAssembly()`、本程序集、
   以及 `AppDomain.CurrentDomain.GetAssemblies()` 里所有已加载程序集，按顺序去重扫描 `RePKG.Native.*` 资源。

2. **`%LOCALAPPDATA%` 不可写时自解压会静默失败**，随后 `DllImport("libSkiaSharp")` 会命中系统里别的旧版本（实测加载到 88.1），
   报出难懂的 `TypeInitializationException`。现在：
   - 新增 `REPKG_NATIVE_CACHE` 环境变量覆盖自解压根目录；
   - 新增 `NativeBootstrap.Describe()`（自解压目录、各程序集资源数、已加载/已解压文件、第一条错误）；
   - CLI 在「一个都没加载成功且存在错误」时向 stderr 打印 `[warn] native libraries were not loaded: ...` 并提示设 `REPKG_NATIVE_CACHE`；
   - CLI 启动时把控制台编码切到 UTF-8（仅当连着终端），中文与 `→` 不再乱码。

验证方式（本机）：`REPKG_NATIVE_CACHE=<可写目录> repkg.exe extract ... --lang en` 成功导出 3 个条目（含 TEX→PNG），
`RePKG-Enhanced.exe` 首次启动解出 `av_libglesv2.dll` / `libHarfBuzzSharp.dll` / `libSkiaSharp.dll` 并正常起窗。
