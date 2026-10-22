# 本地表情预览

`native/` 中的 115 张 PNG 来自本机微信 4.1.15.13 的原始 Qt 资源，未修改图像，包括 105 张经典表情和 10 张新版表情。`native-aliases.json` 的新版名称和文件名逐项来自同版本 `newemoji-config.xml`；经典名称来自 DLL 的 `zh_CN` 指针表，并由实际机器码证实同一索引 `i` 对应 `expression_{i+1}.png`，不是按外观或常见列表顺序猜测。例如“色”对应 `expression_3.png`，“调皮”对应 `expression_13.png`。“笑脸”（Happy.png）与“微笑”（expression_1.png）是不同名称。原生图片属于微信素材，不适用下面的 Twemoji 许可；本轮用于用户的个人桌宠原型。

提取来源 DLL SHA256：`10f8e995453e2da46d4f2b5080cd6da1f13cc5147746adc119ceae38cb039de5`。图片保留原始 24×24、48×48 或 64×64 尺寸。运行时不读取微信安装目录。

`native-names.json` 是 147 个带方括号的原始名称字符串数组，例如 `[色]`、`[调皮]`，用于识别本版本的内置表情名称。来源为 105 个编译名称和 `newemoji-config.xml` 的 45 项，去除“抱拳”“再见”“礼物”三个重名。这是已定义的名称集合，不代表它们全部显示在当前表情面板中；实际写入仍须检查微信草稿。“抱拳”“再见”“礼物”的预览保留已证实的经典图片，没有推断新版覆盖顺序。

只有核实过名称映射的表情使用微信原图。其余微信名称保持文字，不用通用图标替代。

图像采用 Twemoji v14.0.2 的原始 72×72 PNG，作者 Twitter, Inc. and other contributors，按 CC BY 4.0 使用。

- 来源：https://github.com/twitter/twemoji/tree/v14.0.2/assets/72x72
- 许可：https://creativecommons.org/licenses/by/4.0/
- 随包完整许可：licenses/twemoji-LICENSE-GRAPHICS.txt
- aliases.json 是原有通用 emoji 图像索引，当前只用于 Unicode emoji 的本义显示，不再将微信文字名称转换为这些通用图标。图片本身未修改。
- 本地图像缺失或未知名称时保留原文。显示层不改写推荐正文，也不改变 Tab 写入内容。不在运行时请求网络。
