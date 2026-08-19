"""pythonnet 运行时引导：加载 .NET 10 CoreCLR 并引用 Gbar.Net.dll。

必须在任何 `from Gbar.Net...` 导入之前执行（由包 __init__ 最先触发）。

pythonnet 说明：
  - clr_loader + runtimeconfig.json 加载 .NET 10 CoreCLR 运行时（Gbar.Net 目标为 net10.0）；
  - Gbar.Net.* 命名空间由 pythonnet 在运行时从 DLL 动态生成，
    Pylance 静态分析看不到属于正常现象，故 import 行标记 type: ignore。
"""

import os
from pathlib import Path

BASE = Path(__file__).resolve().parent.parent  # Gbar.Python/
DLL_DIR = BASE.parent / "Gbar.Net" / "bin" / "Debug" / "net10.0"

# 让 CoreCLR 能从 DLL 目录解析 LiteNetLib / MemoryPack 等依赖
os.environ["PATH"] = str(DLL_DIR) + os.pathsep + os.environ["PATH"]

from clr_loader import get_coreclr
from pythonnet import set_runtime

set_runtime(get_coreclr(runtime_config=str(BASE / "runtimeconfig.json")))

import clr

clr.AddReference(str(DLL_DIR / "Gbar.Net.dll"))
