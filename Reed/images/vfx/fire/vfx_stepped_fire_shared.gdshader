// 共享火焰：多簇并排时让它们看起来是同一团火。
//
// 原来的 mix 版本有三个「露馅」的地方：
//   1) 颜色按每簇自己的 UV.y 取 → 各簇缩放不同，同一世界高度颜色不一样；
//   2) 噪声按每簇自己的 UV 采样 → 图案在世界空间里是断开的，能看出四份；
//   3) 两层加法 sprite 叠在上面 → 交叠处 0.5+0.5，更亮。
// 这里把高度和噪声都换到世界坐标（vertex 里传 varying），内焰亮度烘进同一层，
// 于是跨簇的交叠只剩 alpha 并集，颜色与花纹都是连续的。
shader_type canvas_item;
render_mode blend_mix;

varying vec2 world_pos;

uniform vec4 OuterColor : source_color = vec4(1.0, 0.166667, 0.0, 1.0);
uniform vec4 InnerColor : source_color = vec4(0.9, 0.40125, 0.045, 1.0);
// 0 = 顶部，1 = 根部。
uniform vec2 InnerColorStep = vec2(0.35, 0.80);
uniform vec4 CoreColor : source_color = vec4(1.0, 0.62, 0.20, 1.0);
uniform vec2 CoreStep = vec2(0.45, 0.90);
uniform float CoreStrength = 0.3;
uniform vec2 OuterStep = vec2(0.135, 0.175);
// 底部填充的权重：太高身体会变成一整块实心色板。
uniform float FillWeight = 0.7;
// 噪声对 alpha 的贡献。这块噪声是三角碎纹，权重一大，mix 混合下就会碎成纸片；
// 让它只负责「边缘的毛刺」，主体交给底部填充。
uniform float NoiseWeight = 0.45;
// 颜色的「热度」来源：0 = 只看世界高度，1 = 只看噪声。中间值兼顾纵向渐变与内部斑驳。
uniform float HeightMix = 0.28;
// 整体不透明度：这层是盖在敌人身上的，太实会把生物整个遮住。
uniform float AlphaScale = 0.85;
// 外缘描边：alpha 低到这个值之前逐渐压回 OuterColor*RimDarken（深红），
// 因为 alpha 是跨簇连续的，描边只会出现在整团火的外轮廓，不会在交叠处互相画线。
uniform float RimSoftness = 0.80;
uniform float RimDarken = 0.55;
// 描边宽度：body 低于这个区间就算「稀薄边缘」。body 只有 fill 与噪声，不含每簇轮廓，
// 所以这条描边跨簇连续，不会在交叠处画出各簇自己的边。
uniform vec2 RimBodyStep = vec2(0.55, 0.95);
// true = 原版「热度取自 v」的配色（火心亮、边缘红，但交叠处会露出各簇的边）；
// false = 世界空间配色 + 按 alpha 压出的外轮廓深红（交叠处无缝）。
uniform bool ColorFromV = true;

// 世界坐标锚点：火焰根部位置与火焰总高度（像素）。所有簇共用同一组。
uniform vec2 GroundOrigin = vec2(0.0, 0.0);
uniform float FlameHeight = 300.0;

// 噪声的「世界周期」（像素）。原版是按 UV*NoiseNScaling 采样，换算到屏幕大约是
// 300 / 430 像素一个周期；世界坐标版本沿用这个量级，太密会把贴图里的三角碎边放大成「碎玻璃」。
uniform float Noise1Strength = 1.0;
uniform vec2 Noise1Tile = vec2(300.0, 300.0);
uniform vec2 Noise1Panning = vec2(1.0, 0.7);
uniform sampler2D Noise1Texture : repeat_enable;
uniform float Noise2Strength = 1.0;
uniform vec2 Noise2Tile = vec2(430.0, 430.0);
uniform vec2 Noise2Panning = vec2(-0.2, 0.9);
uniform sampler2D Noise2Texture : repeat_enable;

uniform float Distortion1Strength = 0.095;
uniform vec2 Distortion1Scale = vec2(1.0, 1.0);
uniform vec2 Distortion1Panning = vec2(0.23, 0.8);
uniform sampler2D Distortion1Texture : repeat_enable;
uniform float Distortion2Strength = 0.1;
uniform vec2 Distortion2Scale = vec2(1.0, 1.0);
uniform vec2 Distortion2Panning = vec2(0.0, 0.76);
uniform sampler2D Distortion2Texture : repeat_enable;

uniform int DebugMode = 0;
uniform sampler2D BottomMask : repeat_disable;
uniform vec2 BottomMaskScale = vec2(1.0, 1.4);
uniform vec2 BottomMaskOffset = vec2(0.0, 0.235);


void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;
}


void fragment() {
	// 贴图扭曲（每簇各自一份，只影响轮廓细节）
	vec2 d1 = texture(Distortion1Texture, UV * Distortion1Scale + Distortion1Panning * TIME).xy;
	vec2 d2 = texture(Distortion2Texture, UV * Distortion2Scale + Distortion2Panning * TIME).xy;
	vec2 off = d1 * Distortion1Strength + d2 * Distortion2Strength;

	// 轮廓：仍用每簇自己的贴图，保证有焰舌形
	float shape = texture(TEXTURE, UV + off).b;

	// 噪声按世界坐标采样 → 所有簇共用同一张花纹，交叠处自然对齐。
	// 这里不能再叠加任何「每簇一份」的抖动：off 里带着各自簇的 Distortion*Panning，
	// 拿它偏移世界坐标会让各簇的噪声随时间互相错开，交界处又露出缝。
	vec2 wpx = world_pos - GroundOrigin;
	vec2 wuv = wpx;
	float n1 = texture(Noise1Texture, wuv / Noise1Tile + Noise1Panning * TIME).b;
	float n2 = texture(Noise2Texture, wuv / Noise2Tile + Noise2Panning * TIME).b;
	float noise = n1 * Noise1Strength * n2 * Noise2Strength;

	// 底部填充：竖直渐变，也按世界高度取，避免各簇高度不同导致底边不齐。
	// BottomMask 的 b 在顶部为 1、底部为 0.2，所以取样时要按「贴图 v 轴」翻转：贴图底部 = 地面。
	float hgt = clamp(-wpx.y / max(1.0, FlameHeight), 0.0, 1.0);
	float my = clamp((1.0 - hgt) * BottomMaskScale.y + BottomMaskOffset.y, 0.0, 1.0);
	float fill = 1.0 - texture(BottomMask, vec2(0.5, my)).b;

	// fill 决定「实心身体」，噪声只在上缘添毛刺。
	float body = fill * FillWeight + noise * NoiseWeight;
	float b2 = smoothstep(0.05, 0.50, body);
	float v = body * shape;

	float a = smoothstep(OuterStep.x, OuterStep.y, v);
	// 轮廓贴图很淡的地方（各簇的外缘）直接抹掉，免得顶部出现飘着的碎块
	a *= smoothstep(0.04, 0.28, shape);

	vec3 col;
	if (ColorFromV) {
		// 原版：热度来自 v（含每簇自己的轮廓），火心亮、边缘压回深红。
		float heat = mix(1.0 - hgt, smoothstep(0.05, 0.45, v), HeightMix);
		col = mix(OuterColor.rgb, OuterColor.rgb + InnerColor.rgb,
				smoothstep(InnerColorStep.x, InnerColorStep.y, heat));
		col += CoreColor.rgb * smoothstep(CoreStep.x, CoreStep.y, v) * CoreStrength;
	} else {
		// 世界空间配色：body 在所有簇之间连续，交叠处颜色完全一致（叠在一起也看不出来）。
		float heat = mix(1.0 - hgt, b2, HeightMix);
		col = mix(OuterColor.rgb, OuterColor.rgb + InnerColor.rgb,
				smoothstep(InnerColorStep.x, InnerColorStep.y, heat));
		col += CoreColor.rgb * smoothstep(CoreStep.x, CoreStep.y, heat) * CoreStrength;
		// 外轮廓深红：body 稀薄的地方（焰舌边缘）与 alpha 渐隐那一层都压暗。
		// 两者都是世界空间连续量，所以描边只会顺着整团火的外形走，不会在交叠处互相画线。
		float edge = max(smoothstep(RimBodyStep.x, RimBodyStep.y, b2),
				smoothstep(0.0, RimSoftness, a));
		col = mix(OuterColor.rgb * RimDarken, col, edge);
	}
	COLOR = vec4(col, a * AlphaScale);

	// 调试：1=shape, 2=noise, 3=fill, 4=v, 5=世界坐标
	if (DebugMode == 1) COLOR = vec4(vec3(shape), 1.0);
	else if (DebugMode == 2) COLOR = vec4(vec3(noise), 1.0);
	else if (DebugMode == 3) COLOR = vec4(vec3(fill), 1.0);
	else if (DebugMode == 4) COLOR = vec4(vec3(v), 1.0);
	else if (DebugMode == 5) COLOR = vec4(vec3(hgt), 1.0);
	else if (DebugMode == 6) COLOR = vec4(my, fill, 0.0, 1.0);
	else if (DebugMode == 7) COLOR = vec4(vec3(b2), 1.0);
}
