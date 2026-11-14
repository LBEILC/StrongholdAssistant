// 界面字重三档（v0.9.5）：[正文, 半粗, 粗体]。MiSans 可变字体最粗到 700；
// 只有常规和粗体两档的系统字体（如微软雅黑）选「稍粗」时正文不变、半粗变粗体，选「加粗」时正文也变粗体
export const FONT_WEIGHTS: Record<string, [number, number, number]> = {
  normal: [400, 600, 700],
  medium: [500, 650, 700],
  bold: [600, 700, 800],
}

/** 界面字体的 CSS 写法：选的字体在前，缺字回落到随包的 MiSans；空串就是默认 */
export const fontStack = (f: string) => (f ? `"${f.replace(/["\\]/g, '')}", 'MiSans'` : `'MiSans'`)
