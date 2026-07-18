# Точне підключення погодженої теми STALKER — v4

Джерело графіки: **лише** погоджений ескіз `approved-full-reference.png` 1672×941.
Нові пейзажі, символи, метал або рамки не генеруються.

## Фактична структура фінальної програми

Патч перевірено проти `MainWindow.xaml`, витягнутого з PDB фінальної збірки `e3afb0ef8da5dfa306e67bea341e2e691b84f1c5`.
Реальне дерево інтерфейсу:

- `DesignSurface`
- `DashboardBlocksGrid`
- `TopBlocksGrid`
- `BottomBlocksGrid`
- `FooterBlocksGrid`

## Координати погодженого макета

| Блок | X | Y | W | H |
|---|---:|---:|---:|---:|
| ChatBlockPanel | 5 | 145 | 816 | 298 |
| DonationsBlockPanel | 831 | 145 | 836 | 298 |
| MixerBlockPanel | 5 | 449 | 816 | 208 |
| NotificationsBlockPanel | 831 | 449 | 836 | 208 |
| SystemStatusBlockPanel | 5 | 664 | 583 | 263 |
| CenterZonePanel | 590 | 660 | 445 | 267 |
| SystemMonitorPanel | 1030 | 664 | 637 | 263 |

## Як використані текстури

- `header-full-exact.png` — точна шапка 1672×145 під живими кнопками.
- `*-shell.png` — оболонки панелей із точними рамками/болтами/заголовками. Динамічні приклади з ескізу прибрані й замінені тільки точним `panel-fill-dark.png` з цього ж ескізу.
- `center-zone-panel-exact.png` — точний центральний банер.
- `outer-*.png` — точна зовнішня рамка.

Живі чати, донати, мікшер, статуси та AIDA64 залишаються WPF-контролами поверх цих оболонок.
