# bundled-themes

存放二开时随包分发的第三方全屏主题（含本地修改），避免它们只存在于被 gitignore 的 `bin/` 目录里而丢失。

## PS5Like（本地修改版）

第三方全屏主题（作者非本仓库），本地对 `Views/Main.xaml` 做了修改，解决"全屏主界面大图偏下、上方留黑框"的问题：

- `PART_ImageBackground`（FadeImage）：`Grid.Row="2" RowSpan="1"` → `Grid.Row="0" RowSpan="3" VerticalAlignment="Stretch"`，让大图从顶部铺满整个可视区（原本只占下半屏）。
- `ShadowTopToBottom`（顶部渐变阴影带）：`Background` 由 `{DynamicResource ShadowTopToBottom}` 改为 `Transparent`。该阴影带原为"图在下半屏"设计，图铺满后它会在中间压出一条黑带；改透明即消除，且不动其布局参数（避免顶部工具栏 f/z/时间/齿轮 错位）。

打包时由 `build/` 下的合并脚本把本目录的主题合入 Release 输出（详见打包流程）。若上游/主题作者更新，需重新套用上述两处修改。
