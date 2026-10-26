// 宽幅火焰：一张 sprite 覆盖整个足迹，形状只由噪声 + 竖直偏置生成。
// 为什么不用原版 shader：原版靠 fire_base_pointy 贴图当轮廓、靠 NoiseMask 当底部填充，
// 横向拉宽后轮廓跟着变宽（圆顶），而且多簇并排时每簇各有一份 v 场，
// 交叠处 alpha 相加 (a1 + a2 - a1*a2) 必然更不透明、各自还有一圈 OuterColor 描边。
// 这里改成单一 v 场 → 整团火只有一份 alpha、一圈描边。
shader_type canvas_item;
render_mode blend_mix;

uniform vec4 OuterColor : source_color = vec4(1.0, 0.166667, 0.0, 1.0);
uniform vec4 InnerColor : source_color = vec4(0.9, 0.40125, 0.045, 1.0);
uniform vec4 CoreColor : source_color = vec4(1.0, 0.85, 0.35, 1.0);
// 颜色渐变的 v 区间：低于 x 全是 OuterColor，高于 y 全是 InnerColor。
uniform vec2 InnerColorStep = vec2(0.18, 0.55);
// 内焰（加亮）的 v 区间与强度。
uniform vec2 CoreStep = vec2(0.55, 0.95);
uniform float CoreStrength = 0.5;
// 轮廓的 v 阈值区间（窄 = 硬边）。
uniform vec2 OuterStep = vec2(0.135, 0.175);
// 底部的竖直偏置：0.0 = 顶、1.0 = 底。MaskStrength 越高底部越实。
uniform float MaskStrength = 0.55;
uniform float MaskStart = 0.0;
uniform float MaskPower = 1.5;
// 噪声整体增益，用来调「焰舌的粗细/密度」对轮廓的影响。
uniform float NoiseGain = 1.0;
// 左右两端的淡出宽度（UV 比例），避免 sprite 边缘被硬切。
uniform float EdgeFade = 0.06;

uniform float Noise2Strength = 1.0;
uniform vec2 Noise2Scaling = vec2(1.4, 1.4);
uniform vec2 Noise2Panning = vec2(-0.2, 0.9);
uniform sampler2D Noise2Texture : repeat_enable;
uniform float Noise1Strength = 1.0;
uniform vec2 Noise1Scaling = vec2(2.0, 2.0);
uniform vec2 Noise1Panning = vec2(1.0, 0.7);
uniform sampler2D Noise1Texture : repeat_enable;

uniform vec2 Distortion2Scale = vec2(1.0, 1.0);
uniform vec2 Distortion2Panning = vec2(0.0, 0.76);
uniform float Distortion2Strength = 0.1;
uniform sampler2D Distortion2Texture : repeat_enable;
uniform vec2 Distortion1Scale = vec2(1.0, 1.0);
uniform vec2 Distortion1Panning = vec2(0.23, 0.8);
uniform float Distortion1Strength = 0.095;
uniform sampler2D Distortion1Texture : repeat_enable;


void fragment() {
	vec2 duv1 = UV * Distortion1Scale + Distortion1Panning * TIME;
	vec2 duv2 = UV * Distortion2Scale + Distortion2Panning * TIME;
	vec2 offset = (texture(Distortion1Texture, duv1).xy * Distortion1Strength
			+ texture(Distortion2Texture, duv2).xy * Distortion2Strength);

	// 扭曲同样作用到噪声采样，边缘才不是一潭死水
	vec2 nuv = UV + offset;
	float n1 = texture(Noise1Texture, nuv * Noise1Scaling + Noise1Panning * TIME).b;
	float n2 = texture(Noise2Texture, nuv * Noise2Scaling + Noise2Panning * TIME).b;
	float noise = n1 * Noise1Strength * n2 * Noise2Strength * NoiseGain;

	// 竖直偏置：底部实、顶部散，顶端的锯齿完全交给噪声决定
	float h = 1.0 - UV.y;
	float mask = clamp((h - MaskStart) / max(0.0001, 1.0 - MaskStart), 0.0, 1.0);
	mask = pow(mask, MaskPower) * MaskStrength;

	float v = noise + mask;

	vec3 col = mix(OuterColor.rgb, InnerColor.rgb, smoothstep(InnerColorStep.x, InnerColorStep.y, v));
	col += CoreColor.rgb * smoothstep(CoreStep.x, CoreStep.y, v) * CoreStrength;

	float a = smoothstep(OuterStep.x, OuterStep.y, v);
	a *= smoothstep(0.0, EdgeFade, UV.x) * smoothstep(0.0, EdgeFade, 1.0 - UV.x);

	COLOR = vec4(col, a);
}
