// 界面皮肤：跟着皮肤换的位图（盟约圆盘、等阶角标、卡框、调度中心等级牌）从 assets/game/skin/<id>/ 取，
// 颜色等令牌由 app.css 按 data-skin 覆盖。跟着设置里的「界面主题」（默认跟随游戏主题，App.svelte），目录名和 GameTheme 一致。
// 可露希尔另有 tier6_mask / tier6_grad / tier_star：6 阶「VI」和 5、6 阶的星星（TierBadge）。
// 文件和游戏素材的对应（游戏目录 autochessvariantskin/[uc]<皮肤>/，终端是 ac_n_initial）：
//   bond_on / bond_off ← prefabs/bondicon/bond_icon_view_item/bg_active / bg_inactive
//   tier_N            ← arts/chesslevelicon/chess_level_icon_N
//   frame / frame_5 / frame_6 ← arts/chessframe/chess_frame_normal / level_five / level_six
//   level_N           ← arts/playerdetailshoplevel/shop_level_N
import type { GameTheme } from './api/types'

export const skin = $state<{ id: GameTheme }>({ id: 'terminal' })
