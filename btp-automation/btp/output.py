"""Output formatting helpers (table, JSON, YAML)."""
import json
from typing import Any, List

try:
    from rich.console import Console
    from rich.table import Table
    from rich import print as rprint
    from rich.markup import escape as _escape
    _RICH = True
except ImportError:
    _RICH = False
    def _escape(s): return s

try:
    import yaml as _yaml
    _YAML = True
except ImportError:
    _YAML = False

console = Console() if _RICH else None


def print_table(data: List[dict], columns: List[str] = None, title: str = ""):
    if not data:
        print("(no results)")
        return
    if _RICH:
        cols = columns or list(data[0].keys())
        t = Table(title=title, show_header=True, header_style="bold cyan")
        for col in cols:
            t.add_column(col)
        for row in data:
            t.add_row(*[str(row.get(c, "")) for c in cols])
        console.print(t)
    else:
        cols = columns or list(data[0].keys())
        header = " | ".join(f"{c:<30}" for c in cols)
        print(f"\n{title}")
        print("-" * len(header))
        print(header)
        print("-" * len(header))
        for row in data:
            print(" | ".join(f"{str(row.get(c, '')):<30}" for c in cols))


def print_json(data: Any):
    print(json.dumps(data, indent=2, default=str))


def print_yaml(data: Any):
    if _YAML:
        print(_yaml.dump(data, default_flow_style=False))
    else:
        print_json(data)


def success(msg: str):
    if _RICH:
        console.print(f"[green]✓[/green] {_escape(str(msg))}")
    else:
        print(f"[OK] {msg}")


def error(msg: str):
    if _RICH:
        console.print(f"[red]✗[/red] {_escape(str(msg))}")
    else:
        print(f"[ERROR] {msg}")


def info(msg: str):
    if _RICH:
        console.print(f"[blue]ℹ[/blue] {_escape(str(msg))}")
    else:
        print(f"[INFO] {msg}")


def warn(msg: str):
    if _RICH:
        console.print(f"[yellow]⚠[/yellow] {_escape(str(msg))}")
    else:
        print(f"[WARN] {msg}")
