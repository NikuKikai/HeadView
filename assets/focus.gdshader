shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;
uniform vec2 focus_center = vec2(0.5, 0.5);
uniform float focus_radius = 0.64;
uniform float feather = 0.10;
uniform float blur_lod = 5.5;
uniform float outer_darkness = 0.78;
uniform bool focus_visible = true;

void fragment() {
    vec2 aspect = vec2(SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x, 1.0);
    float distance_to_focus = length((SCREEN_UV - focus_center) * aspect);
    vec3 clear_color = textureLod(screen_texture, SCREEN_UV, 0.0).rgb;
    if (!focus_visible) {
        COLOR = vec4(clear_color, 1.0);
    } else {
        vec3 soft_color = textureLod(screen_texture, SCREEN_UV, blur_lod).rgb;
        float blur_weight = smoothstep(focus_radius, focus_radius + feather, distance_to_focus);
        float dark_weight = smoothstep(focus_radius, focus_radius + 0.25, distance_to_focus);
        vec3 softened = mix(clear_color, soft_color, blur_weight);
        COLOR = vec4(softened * (1.0 - outer_darkness * dark_weight), 1.0);
    }
}
