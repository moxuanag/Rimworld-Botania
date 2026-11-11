using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Botania
{
    /// <summary>
    /// 开发期工具：把原版身体/头部/头盔贴图导出成 PNG，并把轮廓打印成字符画 + 数字。
    ///
    /// 为什么要它：原版贴图在 Assets.zip 里是 BC7 压缩的 DDS，外部解码器（Python/Pillow）
    /// 解出来的数据是错位的，量不出头部真实位置和大小。引擎自己能正确解，所以让游戏解完
    /// 直接把结果交出来。
    ///
    /// 触发条件：模组根目录存在文件 DevDump.request。平时完全不执行。
    /// 产物：_dev\_botania-dev\vanilla_dump\*.png 与 report.txt
    /// </summary>
    [StaticConstructorOnStartup]
    public static class DevTextureDump
    {
        private const string Marker = "DevDump.request";
        private const string OutDir = @"D:\SteamLibrary\steamapps\common\RimWorld\_dev\_botania-dev\vanilla_dump";

        private static readonly string[] Targets =
        {
            "Things/Pawn/Humanlike/Bodies/Naked_Male_south",
            "Things/Pawn/Humanlike/Bodies/Naked_Male_east",
            "Things/Pawn/Humanlike/Bodies/Naked_Male_north",
            "Things/Pawn/Humanlike/Bodies/Naked_Thin_south",
            "Things/Pawn/Humanlike/Bodies/Naked_Fat_south",
            "Things/Pawn/Humanlike/Bodies/Naked_Hulk_south",
            "Things/Pawn/Humanlike/Bodies/Naked_Female_south",
            "Things/Pawn/Humanlike/Heads/Male/Male_Average_Normal_south",
            "Things/Pawn/Humanlike/Heads/Male/Male_Average_Normal_east",
            "Things/Pawn/Humanlike/Apparel/SimpleHelmet/SimpleHelmet_south",
            "Things/Pawn/Humanlike/Apparel/SimpleHelmet/SimpleHelmet_east",
            "Things/Pawn/Humanlike/Apparel/SimpleHelmet/SimpleHelmet_north",
            "Things/Pawn/Humanlike/Apparel/AdvancedHelmet/AdvancedHelmet_south",
            "Things/Pawn/Humanlike/Apparel/FlakVest/FlakVest_Male_south",
            "Things/Pawn/Humanlike/Apparel/FlakVest/FlakVest_Male_east",
        };

        static DevTextureDump()
        {
            string marker = Path.Combine(LoadedModManager.GetMod<BotaniaMod>().Content.RootDir, Marker);
            if (!File.Exists(marker))
            {
                return;
            }

            LongEventHandler.ExecuteWhenFinished(Dump);
        }

        private static void Dump()
        {
            try
            {
                Directory.CreateDirectory(OutDir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("path\tw\th\ta\tb\tc\td\tbboxW\tbboxH");
                foreach (string path in Targets)
                {
                    Texture2D tex = ContentFinder<Texture2D>.Get(path, false);
                    if (tex == null)
                    {
                        sb.AppendLine(path + "\tNOT-FOUND");
                        continue;
                    }

                    Color32[] px = ReadPixels(tex);
                    int w = tex.width;
                    int h = tex.height;
                    int minX = w, minY = h, maxX = -1, maxY = -1;
                    for (int y = 0; y < h; y++)
                    {
                        for (int x = 0; x < w; x++)
                        {
                            if (px[y * w + x].a > 128)
                            {
                                if (x < minX) minX = x;
                                if (x > maxX) maxX = x;
                                if (y < minY) minY = y;
                                if (y > maxY) maxY = y;
                            }
                        }
                    }

                    string name = path.Substring(path.LastIndexOf('/') + 1);
                    SavePng(px, w, h, Path.Combine(OutDir, name + ".png"));
                    sb.AppendLine($"{path}\t{w}\t{h}\t{minX}\t{minY}\t{maxX + 1}\t{maxY + 1}\t{maxX - minX + 1}\t{maxY - minY + 1}");
                    sb.AppendLine(Ascii(px, w, h, 44));
                }

                File.WriteAllText(Path.Combine(OutDir, "report.txt"), sb.ToString());
                Log.Message("[Botania] DevDump 完成 -> " + OutDir);
            }
            catch (Exception e)
            {
                Log.Error("[Botania] DevDump 失败: " + e);
            }
        }

        /// <summary>把任意贴图拷进 RenderTexture 再读回，绕过不可读贴图的限制。</summary>
        private static Color32[] ReadPixels(Texture2D tex)
        {
            RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(tex, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D copy = new Texture2D(tex.width, tex.height, TextureFormat.ARGB32, false);
            copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Color32[] px = copy.GetPixels32();
            UnityEngine.Object.Destroy(copy);
            return px;
        }

        /// <summary>逐像素写 PNG（引擎里没有方便的直接编码，这里用 EncodeToPNG）。</summary>
        private static void SavePng(Color32[] px, int w, int h, string file)
        {
            Texture2D tmp = new Texture2D(w, h, TextureFormat.ARGB32, false);
            tmp.SetPixels32(px);
            tmp.Apply();
            File.WriteAllBytes(file, tmp.EncodeToPNG());
            UnityEngine.Object.Destroy(tmp);
        }

        /// <summary>把 alpha 画成字符画，方便我在日志里"看"轮廓。</summary>
        private static string Ascii(Color32[] px, int w, int h, int cols)
        {
            int rows = Mathf.Max(1, h * cols / w);
            StringBuilder sb = new StringBuilder();
            for (int ry = 0; ry < rows; ry++)
            {
                sb.Append("    ");
                for (int rx = 0; rx < cols; rx++)
                {
                    int sx = rx * w / cols;
                    int sy = ry * h / rows;
                    byte a = px[sy * w + sx].a;
                    sb.Append(a > 150 ? '#' : (a > 50 ? '+' : '.'));
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
