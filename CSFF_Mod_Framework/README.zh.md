# CSFF Mod Framework: 让模组互相兼容

这一页是 [README.md](README.md) 中 "Making mods work together" 一节的简体中文版,写给 Chiwei 和中文模组社区。代码、UID、文件名和标签名保持英文原样,不翻译。如有出入,以英文版为准。

- **适用版本:** 框架 2.29.0
- **游戏版本:** EA 0.69c

## 让模组互相兼容

两个模组可以共用同一张卡牌、同一个配方槽位或同一个标签,而不必知道对方的存在,前提是任何一方都不要求对方必须安装。下面的步骤就是本套模组遵循的顺序,每一步都附有一个真实发布文件的写法。所有共用资源都登记在作者开发仓库的 Interop Registry 中(不在公开的模组仓库里)。

**1. 先用原版标签。** 如果原版已经有一个标签正好表示你要共用的东西,就把它加到你的卡牌上,到此为止:不需要和任何人商量,也与加载顺序无关。本套模组对灯油 (`tag_Oil`)、燃料 (`tag_Fuel`)、安全饮用水 (`tag_WaterSafe`) 等都是这样做的。原版标签的名字见提取出的游戏数据中的 `CardTag.txt`(源码仓库中的 `Documentation/GameData/CSFF-JsonData_Current/ScriptableObjectObjectName/CardTag.txt`)。标签按名字写入,并配上值为 3 的 `*WarpType`。下面是锡焊料卡牌 (`CardData/MetalParts/Item/TinSolder.json`):三个原版标签和一个互通标签。

```json
"CardTagsWarpData": [ "tag_Metal", "tag_Preservable", "tag_TraderInterestX2Metal", "tag_x_Solder" ],
"CardTagsWarpType": 3
```

**2. 原版没有合适的标签时,用互通标签 `tag_x_<Name>`。** 前缀 `x_` 表示这是模组之间共用的标签,也保证它永远不会和原版标签重名。像上面那样写在 `CardTagsWarpData` 里即可,不要为它发布 `ScriptableObject/CardTag/` 文件:第一个提到这个尚不存在的标签的模组会创建它,之后所有写了同名标签的模组拿到的都是同一个标签。框架自带的有 `tag_x_MetalSheet`、`tag_x_MetalNail`、`tag_x_Solder` 和 `tag_x_Brine`。单靠标签填不进配方槽位,也过不了液体检查,因为它们要的是一张卡牌或一个卡牌组,而不是标签。这就是第 3 步。

**3. 在 `TagGroups.json` 中声明卡牌组,不要发布 CardTabGroup 文件。** 在这里声明的组会在加载时重新填充:凡是带有它的任一标签的卡牌(不论来自哪个模组),再加上你用 UID 列出的 `ExtraCards`(未安装的额外卡牌会被跳过)。下面是框架自带的焊料组:

```json
{
  "Name": "GpTag_FW_Solder",
  "Label": "Solder",
  "LabelKey": "CSFFMFW_GpTag_FW_Solder_TabName",
  "Tags": ["tag_x_Solder"],
  "ExtraCards": ["act_tin_solder"]
}
```

蓝图槽位按名字引用这个组(Advanced Copper Tools 的 `CardData/Blueprint/Bp_LargeSaw.json`):

```json
"RequiredTabGroupWarpData": "GpTag_FW_MetalSheet",
"RequiredTabGroupWarpType": 3
```

液体检查则在 `RequiredGroupWarpData` 中写组名。多个模组可以声明同一个组名,声明会合并,所以想把自己的卡牌加进别人的组,只需用同一个名字再声明一次,只写自己的额外卡牌:Advanced Copper Tools 的 `TagGroups.json` 里就有这样一条完整的条目 `{ "Name": "GpTag_FW_MetalSheet", "ExtraCards": ["act_iron_sheet"] }`。组只会扩大能放进槽位的卡牌范围;槽位自身的属性条件仍会逐张卡牌检查。不要为此发布 `ScriptableObject/CardTabGroup/` 文件:名字与原版卡牌组相同,或带有原版卡牌组的 `TabName.ParentObjectID` 的文件,可能顶替掉原版的那个组,框架会对每个这样的文件警告一次 (2.29.0)。要把蓝图放进制作标签页,请用 `BlueprintTabs.json`。

**4. 金属类型分段 (Metal Type)。** 框架的金属片 (Metal Sheet) 和金属钉 (Metal Nail) 把金属种类记在 `SpecialDurability4`(金属类型)里,共六个取值:100 铜,110 幽灵青铜,120 锡,130 锡青铜,140 白青铜,200 铁。只想接收部分金属的槽位要对 `Special4` 设限;下面这个(大锯的金属片槽位)接收 0 到 200 的所有金属:

```json
"Special4": { "Active": true, "FloatValue": 0.0, "MaxValue": 200.0 }
```

加热状态的部件是同一张卡牌,处在 Fuel 的 151 到 300 段(冷的是 0 到 150),所以接收这些部件的槽位不要对 Fuel、Usage 或 Special1 到 Special3 设限,否则加热的和冷的部件就不能同样填进去了。你的模组里的卡牌带上 `tag_x_MetalSheet`、`tag_x_MetalNail` 或 `tag_x_Solder`,就会加入金属片、金属钉和焊料组。如果希望槽位能分辨它的金属,请把 `SpecialDurability4` 按上面的取值作为金属类型提供:Special4 未启用的卡牌会通过所有金属类型的限制。你自己的焊料必须带有 `SpecialDurability1` 作为使用次数 (Uses),因为配方会保留焊料条并只消耗一次使用次数,没有这个属性的卡牌永远用不完。

**5. 重命名或合并卡牌时,用 `UidAliases.json`。** 在 `ModInfo.json` 旁边放一个 `{ "old_uid": "new_uid" }`。之后,无论是你的模组还是别的模组,凡是在 `*WarpData` 中写旧 UID 的,都会解析到新卡牌;存档里保存的旧 UID 也能找到新卡牌(把已移除的环境指向仍存在的环境时,在其中保存的存档也会被一并移过去;已重命名卡牌这一半尚未在运行中的游戏里确认)。只有已不存在的 UID 才会被别名,所以仍然是真实卡牌的旧 UID 绝不会被遮盖。下面是框架自己那个文件里的几行,它把 Chiwei 的金属部件 ID 映射到框架的卡牌上:

```json
{
  "APE_MetalSheet":       "advanced_copper_tools_metal_sheet",
  "APE_MetalSolder_Ball": "4b0f4937a5ecb90499428c8c10288afc"
}
```

如果两个模组把同一个旧 UID 别名到不同的目标,加载顺序靠前的保留,日志会给出警告。如果你的模组发布的卡牌 UID 与框架也发布的相同,你的会被加载,框架会跳过自己的那一份。要把两个蓝图合并为一个时,请把旧文件留作退役占位:不放进 `BlueprintTabs.json`,清空 `CardsOnBoard`,让 `AlsoUnlocksWarpData` 指向合并后的蓝图。这样旧配方做到一半的工地仍能完成,已学会旧配方的存档也保留研究进度。

**6. 用 `_appendArrays` 追加,不要替换。** `GameSourceModify` 补丁在以下情况会替换字段:直接写字段名,或写 `<Field>WarpData` 且 WarpType 为 3 或 5;用 `_appendArrays`,或 WarpType 为 4 或 6,则是追加。替换列表会抹掉原版或别的模组放进去的内容。下面是框架让硬脂块 (Stearin Cake) 成为原版蜡烛可用的蜡的完整补丁 (`GameSourceModify/GpTag_FatOrWax.json`,文件名就是被补丁的组名):

```json
{
  "_appendArrays": {
    "IncludedCards": ["APE_StearinCake"]
  }
}
```

要给某张卡牌的 `CardInteractions` 追加内容,请写 `CardInteractionsWarpData` 并配上 `"CardInteractionsWarpType": 4`(见 `GameSourceModify/FishMeatFattyRaw.json`)。2.29.0 新增:当两个模组替换同一个对象的同一个字段时,日志会输出一条警告,写明两个模组、对象和字段,并提示改用追加。前 25 条为警告,其余降为调试级别,另有一条汇总警告。该功能受 `AuthoringLint` 开关控制(见上文 Authoring lint)。

**7. 动画用 PNG 帧。** GIF 的每个像素要么全透明要么不透明,所以柔和的阴影或光晕会丢失。把帧作为 PNG 文件放在 `Resource/GIF/<GifName>/` 中,然后像对待 GIF 一样,在 `CardData/Gif/*.json` 中写上 `<GifName>`(下面是传送门的写法,`CardData/Gif/PortalHub.json`,它的动画是一个 GIF 文件):

```json
{
  "CardUniqueId": "csffmfwportalplaced",
  "CardGif": { "GifName": "Portal_Placed", "Loop": true }
}
```

帧按文件名中最后一个数字的大小顺序播放(`flame_2.png` 在 `flame_10.png` 之前),每一帧都保留完整的透明度。每帧 100 毫秒,除非文件夹里有 `frames.json`:`{ "DelayMs": 80 }` 表示所有帧相同,`{ "DelaysMs": [80, 80, 120] }` 表示逐帧设定(列表较短时,后面的帧沿用最后一个值)。请保持所有帧尺寸一致。同名的 GIF 优先,此时该文件夹会被跳过;GIF 名字是所有模组共用的,请加上自己模组的前缀。指南:源码仓库中的 `Documentation/CSFF_GIF_Authoring.md`。框架目前还没有发布任何帧文件夹,所以 `frames.json` 的格式来自加载器代码 (`Gif/GifLoader.cs`)。

**8. 让粉末能倒进容器:`LiquidConversions.json`。** 在 `ModInfo.json` 旁放一个文件,粉末就能像原版的盐和面粉一样,以液体形态倒进罐子、袋子或桶里,再用该液体的"清空"按钮取回。目前还没有已发布的模组提供任何配对;下面是测试用的样例文件 (`Development_Tools/Tests/Fixtures/Framework_LiquidConversions/FW_LiquidConversions_FixtureA/LiquidConversions.json`):

```json
[
    { "Solid": "fwlcfix_a_powder", "Liquid": "fwlcfix_a_lq_powder", "Quantity": 5 }
]
```

`Quantity` 是一张固体卡牌变成的液体单位数(原版一撮盐用 5),必须大于 0。液体是 CT9 类型的卡牌,请给它加上 `tag_PowderLiquid`,这样袋子才会接收。一张卡牌只能出现在一个配对里:按加载顺序,第一个模组保留它,之后写到它的行会被跳过,并有一条写明两个模组的警告;原版自己的配对永远优先。参见上文 Declarative files。

**9. 部件必须匹配时,用 `MatchAcross`。** 蓝图可以要求几个槽位里的卡牌的某个属性值相同,比如一个配方里的金属片和金属钉必须是同一种金属。下面是大锯 (`CardData/Blueprint/Bp_LargeSaw.json`):第 0 阶段的金属片槽位(元素 0)和金属钉槽位(元素 2)的 `Special4` 必须一致。

```json
"MatchAcross": [
  { "Stat": "Special4", "Elements": [ "0.0", "0.2" ] }
]
```

完整规则(元素的引用方式、`Remember`、加载时的警告)见上文 Matching parts across slots。

**10. 卡牌属性不够用时,用 `Api.CardState`。** 一张卡牌只有八个属性,有的已经被占用;`CardState` 能在存档里为单张卡牌保存带名字的数字。只需声明一次:这个值在卡牌变形后保留,并在蓝图中取原料的平均值传给成品。下面是 Advanced Copper Tools 的大锯,它会记住所用部件的锻造品质:

```csharp
CardState.DeclareCarry("act.large_saw.part_quality", true, CardStateMerge.Average, "advanced_copper_tools_large_saw");
CardState.DeclareBlueprintSeed("act.large_saw.part_quality", "SpecialDurability3",
    "advanced_copper_tools_metal_sheet", "advanced_copper_tools_copper_nails");
float quality = CardState.Get(card, "act.large_saw.part_quality");   // 卡牌没有该值时为 NaN
```

每个名字都要加上你自己模组的前缀;`Set` 要在游戏运行中调用,不能在主菜单调用。其余内容见下文 C# API。

**11. 模组 CSV 中的转义序列。** 模组 `Localization/*.csv` 的值里写的字面 `\n`(以及 `\r\n`)现在会显示为换行,英文列和中文列都一样,和原版文本一致。`SimpCn.csv` 的每一行是 Key、英文、中文,所以 `Key,"First line.\nSecond line.",第一行。\n第二行。` 在两种语言里都是两行。带引号的值里真正的换行照常有效,单独的 `\r` 不处理,也没有办法显示字面的反斜杠加 n。中文列留空时显示英文文本。

**12. 要共用新东西,先申请在 Interop Registry 中加一行。** Interop Registry(在作者的开发仓库中,不在公开的模组仓库里)列出模组之间共用的每一种资源,每种一行(R 行):资源、层级、所有者、生产者和消费者。层级有 V(原版标签)、X(互通标签 `tag_x_*`)和 U(仅在对方模组存在时才使用的卡牌 UID,用于标签表达不了的情况:交易行、替代原料、掉落或配方注入)。例如 R26 到 R29 就是上面的金属部件和盐水标签。一行的所有者(是一个模组,不是某个人)决定这种资源的含义,比如单位和哪个属性存什么。生产者可以不经询问就给自己的卡牌打标签,消费者在自己一侧提供接收的实现。S 行说明哪个模组拥有某个属性,C 行是对概念和 UID 的认领,以免两个模组做出同一张卡牌;UID 属于发布它的模组,而不是看前缀。内容类模组不对另一个模组设硬性依赖:对方模组的 UID 只出现在缺失卡牌会被跳过的位置(掉落槽位、列表),绝不放进必需的原料里。要为你的模组提出新的一行,请在框架的 Nexus 页面上发布资源、标签或 UID、所有者定义什么、以及谁生产谁消费;作者会加上这一行,框架文档也会写明。
