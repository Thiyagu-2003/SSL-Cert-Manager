using System;
using System.IO;
using System.Text;
using Xunit;

namespace CertManager.UnitTests;

public class LogoGeneratorTests
{
    [Fact]
    public void GenerateCertFlowAssets()
    {
        string assetsDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/CertManager.Desktop/Assets"));
        Directory.CreateDirectory(assetsDir);

        // SVG Logo of Concept 1: The Infinity Shield
        string svg = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="256" height="256">
          <defs>
            <linearGradient id="shieldGrad" x1="0%" y1="0%" x2="100%" y2="100%">
              <stop offset="0%" stop-color="#0284C7"/>
              <stop offset="50%" stop-color="#2563EB"/>
              <stop offset="100%" stop-color="#4F46E5"/>
            </linearGradient>
            <linearGradient id="flowGrad" x1="0%" y1="100%" x2="100%" y2="0%">
              <stop offset="0%" stop-color="#22D3EE"/>
              <stop offset="50%" stop-color="#38BDF8"/>
              <stop offset="100%" stop-color="#FFFFFF"/>
            </linearGradient>
            <filter id="glow" x="-20%" y="-20%" width="140%" height="140%">
              <feDropShadow dx="0" dy="6" stdDeviation="12" flood-color="#0284C7" flood-opacity="0.35"/>
            </filter>
          </defs>

          <!-- Outer Shield with Glow -->
          <path d="M 128,18 
                   C 190,44 220,52 220,96 
                   C 220,166 172,216 128,238 
                   C 84,216 36,166 36,96 
                   C 36,52 66,44 128,18 Z" 
                fill="url(#shieldGrad)" 
                filter="url(#glow)"/>

          <!-- Inner Shield Border Highlight -->
          <path d="M 128,28 
                   C 182,51 208,58 208,98 
                   C 208,158 166,203 128,223 
                   C 90,203 48,158 48,98 
                   C 48,58 74,51 128,28 Z" 
                fill="none" 
                stroke="#38BDF8" 
                stroke-width="3" 
                opacity="0.4"/>

          <!-- Dynamic Flow Waves (CertFlow Core Loop) -->
          <!-- Wave 1: Flow Loop -->
          <path d="M 78,138 
                   C 78,112 96,96 118,96 
                   C 134,96 146,106 156,120 
                   C 166,134 178,144 194,144 
                   C 216,144 234,128 234,102"
                fill="none" 
                stroke="url(#flowGrad)" 
                stroke-width="12" 
                stroke-linecap="round"/>

          <!-- Central Security Padlock Node -->
          <circle cx="128" cy="120" r="14" fill="#FFFFFF"/>
          <path d="M 123,120 L 123,113 C 123,110 125,108 128,108 C 131,108 133,110 133,113 L 133,120" 
                fill="none" stroke="#0284C7" stroke-width="2.5" stroke-linecap="round"/>
          <circle cx="128" cy="122" r="2.5" fill="#0284C7"/>
        </svg>
        """;

        File.WriteAllText(Path.Combine(assetsDir, "certflow_logo.svg"), svg, Encoding.UTF8);

        // Generate a valid multi-size Windows .ICO file directly containing embedded PNG representations
        // Let's create an uncompressed 32-bit BMP or PNG encoded ICO
        CreateValidIcoFile(Path.Combine(assetsDir, "certflow.ico"));
    }

    private static void CreateValidIcoFile(string destinationPath)
    {
        // Generate a 48x48 32bpp RGBA icon with the shield & flow graphics
        int width = 48;
        int height = 48;
        byte[] xorMask = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width + x) * 4;
                // Distance from center
                double dx = (x - 24.0) / 20.0;
                double dy = (y - 24.0) / 20.0;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                // Shield-like shape
                bool inside = (dist <= 1.0);
                if (inside)
                {
                    // Gradient: Secure Emerald Green to Mint Cyan (#00F5A0 -> #10B981)
                    byte b = (byte)(160 - (x * 2));
                    byte g = (byte)(230 - (y / 2));
                    byte r = (byte)(16 + (x / 2));
                    byte a = 255;

                    xorMask[index + 0] = b; // Blue
                    xorMask[index + 1] = g; // Green
                    xorMask[index + 2] = r; // Red
                    xorMask[index + 3] = a; // Alpha
                }
                else
                {
                    // Transparent
                    xorMask[index + 0] = 0;
                    xorMask[index + 1] = 0;
                    xorMask[index + 2] = 0;
                    xorMask[index + 3] = 0;
                }
            }
        }

        // Draw an inner lock / shackle
        for (int y = 14; y <= 26; y++)
        {
            for (int x = 18; x <= 30; x++)
            {
                int index = (y * width + x) * 4;
                if ((x >= 22 && x <= 26 && y >= 18 && y <= 24) || (y == 16 && x >= 20 && x <= 28))
                {
                    xorMask[index + 0] = 255; // White
                    xorMask[index + 1] = 255;
                    xorMask[index + 2] = 255;
                    xorMask[index + 3] = 255;
                }
            }
        }

        // Draw the prominent verified security checkmark in bright mint (#00F5A0)
        for (int i = 0; i < 5; i++)
        {
            int cx = 19 + i;
            int cy = 29 + i;
            for (int dy = -1; dy <= 1; dy++)
            {
                int index = ((cy + dy) * width + cx) * 4;
                if (index >= 0 && index + 3 < xorMask.Length)
                {
                    xorMask[index + 0] = 160;
                    xorMask[index + 1] = 245;
                    xorMask[index + 2] = 0;
                    xorMask[index + 3] = 255;
                }
            }
        }
        for (int i = 0; i < 11; i++)
        {
            int cx = 24 + i;
            int cy = 34 - i;
            for (int dy = -1; dy <= 1; dy++)
            {
                int index = ((cy + dy) * width + cx) * 4;
                if (index >= 0 && index + 3 < xorMask.Length)
                {
                    xorMask[index + 0] = 160;
                    xorMask[index + 1] = 245;
                    xorMask[index + 2] = 0;
                    xorMask[index + 3] = 255;
                }
            }
        }

        using var fs = new FileStream(destinationPath, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // ICONDIR
        bw.Write((ushort)0); // reserved
        bw.Write((ushort)1); // type 1 = ICO
        bw.Write((ushort)1); // 1 image

        // ICONDIRENTRY
        int bitmapHeaderSize = 40;
        int andMaskSize = ((width + 31) / 32) * 4 * height;
        int imageSize = bitmapHeaderSize + xorMask.Length + andMaskSize;

        bw.Write((byte)width);
        bw.Write((byte)height);
        bw.Write((byte)0); // colors
        bw.Write((byte)0); // reserved
        bw.Write((ushort)1); // planes
        bw.Write((ushort)32); // bit count
        bw.Write(imageSize); // bytes in res
        bw.Write(22); // offset (6 + 16 = 22)

        // BITMAPINFOHEADER (height is doubled in ICO format for XOR + AND masks)
        bw.Write(bitmapHeaderSize);
        bw.Write(width);
        bw.Write(height * 2);
        bw.Write((ushort)1);
        bw.Write((ushort)32);
        bw.Write(0); // BI_RGB
        bw.Write(xorMask.Length + andMaskSize);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);

        // XOR mask (bottom-up BMP order)
        for (int row = height - 1; row >= 0; row--)
        {
            bw.Write(xorMask, row * width * 4, width * 4);
        }

        // AND mask (all 0s because 32bpp alpha handles transparency)
        byte[] andMask = new byte[andMaskSize];
        bw.Write(andMask);
    }
}
