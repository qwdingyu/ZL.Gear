# Markdown File

各有各的好处，之前手动定义描述json，即使修改编辑器参数，不用修改代码就可以实现。现在如果从代码生成json，就必须要保证 代码和json统一，否则手工调整json后，代码ConfigParameter没有修正，下次再通过ConfigParameter生成后就会出现覆盖的情况。


这是一个非常棒的现实问题。这触及到了**“开发态（Design Time）”与“运行态（Runtime）”**的边界管理。
你的顾虑非常正确：
代码优先（Code First）：保证了类型安全和参数不丢失，但修改默认值或文案需要重新编译发布，灵活性差。
配置优先（Schema First）：修改灵活，但容易与代码逻辑脱节（比如代码删除了参数 LCL，配置里还有，运行时报错）。
作为架构师，我给出的解决方案是：“代码定义骨架，配置定义皮肉”的混合覆盖策略（Overlay Strategy）。
核心理念：Base + Override (基准 + 覆盖)
我们将元数据分为两层：
硬元数据（Hard Metadata）：由 Attribute 定义。
包含：Command, Parameter Key, DataType。
特点：不可通过 JSON 修改。因为这些直接关联到代码逻辑，改了就会崩。
软元数据（Soft Metadata）：由 JSON 定义。
包含：DisplayName, DefaultValue, Unit, Editor, Description, Options。
特点：允许 JSON 覆盖代码定义。
解决方案实现
我们需要改造 MetadataDiscoveryService，使其在扫描完 Attribute 后，尝试读取一个外部的 JSON 文件，将 JSON 中的配置**合并（Merge）**到 Attribute 生成的结果中。
1. 定义覆盖规则
代码生成的列表是全集（保证不会漏参数）。
JSON 文件是补丁集（只包含需要修改的部分）。
加载顺序：Code Attributes -> External JSON -> Final Metadata。
2. 代码实现 (ZL.Gear.Engine)
在 MetadataDiscoveryService 中增加合并逻辑。


工作流设计 (Product Manager 视角)
通过这种**“基准+补丁”**模式，我们定义两种工作流，彻底解决你的痛点。
场景 A：开发人员新增功能 (Code Source of Truth)
开发者编写 NewHandler.cs，加上 [ConfigParameter("Delay", Default=100)]。
编译运行。
系统检测到新的 Attribute，生成的元数据中会自动包含 NewHandler。
因为没有对应的 JSON 补丁，直接使用代码默认值 100。
结果：保证了代码和配置的统一，不会因为忘记写 JSON 而导致参数缺失。
场景 B：现场工程师调整默认值 (Config Flexibility)
现场反馈：默认延时 100 太短了，所有新步骤默认都要 500，且名字要改成“缓冲时间”。
工程师不需要找开发人员重新编译 DLL。
工程师打开 MetadataOverrides.json (或通过 UI 界面编辑元数据)。
写入/修改配置：
code
JSON
[
  {
    "Command": "NewHandler",
    "Parameters": [
      { "Key": "Delay", "DefaultValue": 500, "DisplayName": "缓冲时间" }
    ]
  }
]
重启软件。
MergeMetadata 逻辑生效。虽然代码里写的是 100，但最终 UI 显示的是 500。
下次开发者更新了 DLL（比如修复了 Bug），只要参数 Key 没变，这个 JSON 补丁依然有效！不会被覆盖。
如何避免“生成覆盖”问题？
你之前的痛点是：“下次再通过 ConfigParameter 生成后就会出现覆盖的情况”。
在新的架构下，我们不再生成并覆盖那个主要使用的配置文件。我们将文件分为两个：
Generated_Reference.json (只读/参考)：
每次软件启动（或开发者手动导出）时，根据 Attribute 全量生成。
这个文件不作为配置源，仅作为文档或 UI 编辑器的参考，告诉用户“当前代码里有哪些参数”。
每次都会被无情覆盖。
Metadata_Overrides.json (用户配置)：
这个文件由用户/现场工程师维护（手动或通过 UI）。
软件只读取，不自动写入（或者只通过专门的元数据编辑器写入）。
它只包含差异。
UI 维护工具的逻辑：
UI 编辑器加载时，先读 Attribute 拿到全集，再读 Overrides 拿到修改值。用户在 UI 上修改保存时，只把差异部分保存到 Metadata_Overrides.json。
总结
Attribute (代码)：负责“有什么” (Schema: Keys, Types)。这是刚性的，必须和代码一致。
JSON (补丁)：负责“像什么” (Presentation: Defaults, Labels)。这是柔性的，适应现场变化。
Merge 逻辑：在运行时将二者结合。
这样你就拥有了代码的健壮性（不会少参数、类型不会错）和配置的灵活性（现场随意改默认值、文案，且升级 DLL 不丢失配置）。