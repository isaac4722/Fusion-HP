# C++ 核心指南技能

这是一个包含 **C++ Core Guidelines**（C++ 核心指南）的 Claude Code 技能仓库，由 Bjarne Stroustrup 和 Herb Sutter 编写。本技能为编写现代、安全、高效的 C++ 代码（C++11/14/17/20）提供全面指导。

## 概述

C++ 核心指南是一套经过实践检验的编码准则、规则和最佳实践。它们旨在帮助程序员编写：

- **类型安全**：无隐式类型系统违规
- **资源安全**：无泄漏（内存、句柄、锁）
- **高性能**：在不牺牲正确性的前提下保持高效
- **正确性**：在编译期捕获更多逻辑错误

## 目录结构

```
├── SKILL.md              # 主技能入口
├── CLAUDE.md             # Claude Code 仓库指引
└── references/           # 主题特定的指南参考
    ├── memory-safety.md  # RAII、指针、智能指针
    ├── api-design.md     # 接口、函数签名
    ├── class-design.md   # 类、继承层次、零/五法则
    ├── concurrency.md    # 线程安全、数据竞争
    ├── error-handling.md # 异常、错误码
    ├── templates.md      # 模板、STL、泛型
    ├── modern-style.md   # 哲学、命名、风格
    └── performance.md    # 优化、C 兼容性
```

## 使用方法

本技能专为 Claude Code 设计。在处理 C++ 代码时，Claude 会根据任务自动参考相应的指南：

| 任务 | 参考 |
|------|------|
| 内存泄漏、RAII、指针 | `references/memory-safety.md` |
| 函数签名、API | `references/api-design.md` |
| 类设计、构造函数 | `references/class-design.md` |
| 线程安全、并发 | `references/concurrency.md` |
| 异常、错误处理 | `references/error-handling.md` |
| 模板、STL | `references/templates.md` |
| 编码风格、哲学 | `references/modern-style.md` |
| 性能 | `references/performance.md` |

## 指南分类

指南按规则前缀进行组织：

| 前缀 | 类别 |
|------|------|
| P | 哲学 |
| I | 接口 |
| F | 函数 |
| C | 类与类层次 |
| R | 资源管理 |
| ES | 表达式与语句 |
| Per | 性能 |
| CP | 并发 |
| E | 错误处理 |
| T | 模板 |
| SL | 标准库 |
| NL | 命名与布局 |

## 许可证

C++ 核心指南由 C++ 社区维护，按适当的开源条款提供。更多信息请参阅[官方仓库](https://github.com/isocpp/CppCoreGuidelines)。
