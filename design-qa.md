# Workspace design QA

Compared the installed Codex reference and the WPF implementation at 1280 x 1392, with a project conversation selected and the inspector closed. Reference screenshot stays local and is excluded from source uploads. Implementation content is an explicitly labeled synthetic UI fixture, not a copied user conversation.

Fonts/typography: Segoe UI / Malgun Gothic, 20px conversation text, semibold header. Native capture text antialiasing differs from the WPF 96-DPI render. Source title-menu and some icon sizes retain small P3 differences.
Spacing/layout: 76px rail + 358px sidebar, conversation starts near x470, bottom rounded composer near x457. Sidebar/project groups, collapsed activity rows, and panel resizing verified. Inspector automatically hides the sidebar in narrow windows to keep controls usable.
Colors/tokens: neutral light canvas/sidebar/rail and line colors replace the previous navy dashboard. Theme changes preserve all controls.
Assets: supplied Microsoft Fluent UI System Icons are used from their MIT source assets. No generated placeholder logo, copied account avatar, or private screenshot is shipped.
Copy/content: Hermes branding, its project data and actual connection states are intentionally retained. Unavailable features are described in the capabilities dialog and PLAN.md.

Native interaction: title-bar drag, maximize/restore and menu access passed on the real Windows window. 53 WPF control/event checks and real WebView2 fixture checks passed.

No remaining P0/P1/P2 issue in the implemented workspace flow. P3 follow-up: deeper typography and icon parity across secondary dialogs. Full product parity remains unachieved as documented in PLAN.md; this gate covers the implemented workspace only.

final result: passed