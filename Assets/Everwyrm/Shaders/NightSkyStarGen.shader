// Gerador do cubemap de estrelas do Everwyrm — roda UMA vez no load via
// Graphics.Blit por face (fora do pipeline de render do HDRP; só escreve
// textura). O resultado vai no spaceEmissionTexture do Physically Based Sky.
//
// Conteúdo por pixel (direção no céu):
//  - 3 camadas de estrelas por grade de células 3D com hash determinístico
//    (posição/brilho/tamanho/temperatura de cor por estrela, seed do mundo);
//  - Via Láctea: faixa em torno de um grande círculo aleatório com fBm
//    (névoa luminosa) + camada extra de estrelas densa dentro da faixa.
// HDR real (>1) para o bloom pegar as mais brilhantes.
Shader "Everwyrm/NightSkyStarGen"
{
    Properties { }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            // base da face do cubemap sendo renderizada (setada pelo C#)
            float3 _FaceX, _FaceY, _FaceZ;
            // xyz: offset de seed · w: não usado
            float4 _SeedOffset;
            // x: densidade de estrelas · y: intensidade estrelas ·
            // z: intensidade Via Láctea · w: meia-largura da faixa (0-1)
            float4 _Params;
            float3 _GalaxyNormal;    // normal do plano galáctico (unit, da seed)

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }

            // ---- hashing determinístico (variante Dave Hoskins p/ domínio
            // GRANDE: o primeiro frac usa constante PEQUENA — com célula ~290,
            // multiplicar por centenas estouraria a precisão do float)
            float3 hash33(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            float hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            // value noise 3D + fBm p/ a névoa da galáxia
            float vnoise(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = hash13(i);
                float n100 = hash13(i + float3(1, 0, 0));
                float n010 = hash13(i + float3(0, 1, 0));
                float n110 = hash13(i + float3(1, 1, 0));
                float n001 = hash13(i + float3(0, 0, 1));
                float n101 = hash13(i + float3(1, 0, 1));
                float n011 = hash13(i + float3(0, 1, 1));
                float n111 = hash13(i + float3(1, 1, 1));
                return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                            lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
            }

            float fbm(float3 p)
            {
                float v = 0.0, a = 0.5;
                for (int i = 0; i < 4; i++)
                {
                    v += a * vnoise(p);
                    p = p * 2.13 + 7.7;
                    a *= 0.5;
                }
                return v;
            }

            // temperatura 0 (fria/vermelha) → 1 (quente/azul), via corpo negro aprox.
            float3 starColor(float t)
            {
                float3 red   = float3(1.0, 0.55, 0.35);
                float3 white = float3(1.0, 0.97, 0.92);
                float3 blue  = float3(0.62, 0.74, 1.0);
                return t < 0.5 ? lerp(red, white, t * 2.0) : lerp(white, blue, t * 2.0 - 1.0);
            }

            // uma camada de estrelas: grade de células no espaço de direções
            float3 starLayer(float3 dir, float scale, float density, float sizeMul)
            {
                float3 p = dir * scale + _SeedOffset.xyz;
                float3 cell = floor(p);
                float3 col = 0;
                [unroll] for (int x = -1; x <= 1; x++)
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int z = -1; z <= 1; z++)
                {
                    float3 cid = cell + float3(x, y, z);
                    float3 rnd = hash33(cid);
                    // nem toda célula tem estrela — densidade controla
                    if (hash13(cid + 91.7) > density) continue;

                    float3 starP = cid + rnd;                 // posição no espaço escalado
                    float d = length(p - starP);              // distância angular aprox.
                    // brilho em lei de potência: muitas fracas, raras brilhantes
                    float mag = hash13(cid + 17.3);
                    float brightness = pow(mag, 9.0) * 24.0 + pow(mag, 3.0) * 0.6 + 0.04;
                    float radius = (0.05 + 0.10 * pow(hash13(cid + 33.1), 2.0)) * sizeMul;
                    float glow = exp(-(d * d) / (radius * radius));
                    float temp = hash13(cid + 55.9);
                    col += starColor(temp) * brightness * glow;
                }
                return col;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 st = i.uv * 2.0 - 1.0;
                float3 dir = normalize(_FaceX * st.x + _FaceY * st.y + _FaceZ);

                float density = saturate(0.28 * _Params.x);
                float3 col = 0;

                // três camadas: fundo fino, média, e estrelas "hero" raras/grandes
                col += starLayer(dir, 160.0, density,        1.0) * 0.35;
                col += starLayer(dir,  72.0, density,        1.0) * 0.8;
                col += starLayer(dir,  30.0, density * 0.6,  1.4) * 1.6;

                // ---- Via Láctea: faixa em torno do grande círculo da seed
                float band = abs(dot(dir, _GalaxyNormal));
                float bandW = max(_Params.w, 0.02);
                float bandMask = exp(-(band * band) / (bandW * bandW));
                if (_Params.z > 0.0 && bandMask > 0.003)
                {
                    // névoa luminosa com estrutura (2 escalas de fBm, recortadas)
                    float neb = fbm(dir * 5.0 + _SeedOffset.xyz * 0.37);
                    neb *= 0.6 + 0.4 * fbm(dir * 13.0 - _SeedOffset.xyz * 0.19);
                    neb = saturate(neb - 0.28) * 1.6;
                    float3 nebCol = lerp(float3(0.55, 0.62, 0.85),   // azul-poeira
                                         float3(0.95, 0.85, 0.75),   // núcleo quente
                                         fbm(dir * 2.3 + 3.1));
                    col += nebCol * neb * bandMask * 0.16 * _Params.z;

                    // adensamento estelar dentro da faixa
                    col += starLayer(dir, 230.0, density, 0.9) * bandMask * 0.5 * _Params.z;
                }

                return float4(col * _Params.y, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
