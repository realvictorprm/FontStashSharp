﻿using FontStashSharp.Interfaces;
using System;
using System.Text;

#if MONOGAME || FNA
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#elif STRIDE
using Stride.Core.Mathematics;
#else
using System.Drawing;
using System.Numerics;
#endif

namespace FontStashSharp
{
	public partial class DynamicSpriteFont: SpriteFontBase
	{
		internal readonly Int32Map<DynamicFontGlyph> Glyphs = new Int32Map<DynamicFontGlyph>();
		internal readonly Int32Map<DynamicFontGlyph> GlyphsWithoutBitmap = new Int32Map<DynamicFontGlyph>();

		public FontSystem FontSystem { get; private set; }

		internal DynamicSpriteFont(FontSystem system, int size): base(size)
		{
			if (system == null)
			{
				throw new ArgumentNullException(nameof(system));
			}

			FontSystem = system;
			RenderFontSizeMultiplicator = FontSystem.FontResolutionFactor;
		}

		internal Int32Map<DynamicFontGlyph> GetGlyphMapFor(bool isForMeasurement)
		{
			return isForMeasurement ? GlyphsWithoutBitmap : Glyphs;
		}

		private DynamicFontGlyph GetGlyphWithoutBitmap(int codepoint, bool withoutBitmap)
		{
			DynamicFontGlyph glyph = null;
			var glyphs = GetGlyphMapFor(withoutBitmap);
			if (glyphs.TryGetValue(codepoint, out glyph))
			{
				return glyph;
			}

			IFontSource font;
			var g = FontSystem.GetCodepointIndex(codepoint, out font);
			if (g == null)
			{
				glyphs[codepoint] = null;
				return null;
			}

			int advance = 0, x0 = 0, y0 = 0, x1 = 0, y1 = 0;
			var fontSize = (int) (FontSize * (withoutBitmap ? 1 : FontSystem.FontResolutionFactor));
			font.GetGlyphMetrics(g.Value, fontSize, out advance, out x0, out y0, out x1, out y1);

			var pad = Math.Max(DynamicFontGlyph.PadFromBlur(FontSystem.BlurAmount), DynamicFontGlyph.PadFromBlur(FontSystem.StrokeAmount));
			var gw = (x1 - x0) + pad * 2 + FontSystem.KernelWidth;
			var gh = (y1 - y0) + pad * 2 + FontSystem.KernelHeight;
			var offset = DynamicFontGlyph.PadFromBlur(FontSystem.BlurAmount);

			glyph = new DynamicFontGlyph
			{
				Codepoint = codepoint,
				Id = g.Value,
				Size = fontSize,
				Font = font,
				Bounds = new Rectangle(0, 0, gw, gh),
				XAdvance = advance,
				XOffset = x0 - offset,
				YOffset = y0 - offset
			};

			glyphs[codepoint] = glyph;

			return glyph;
		}

		private DynamicFontGlyph GetGlyphInternal(int codepoint, bool withoutBitmap)
		{
			var glyph = GetGlyphWithoutBitmap(codepoint, withoutBitmap);
			if (glyph == null)
			{
				return null;
			}

			if (withoutBitmap || glyph.Texture != null)
				return glyph;

			FontSystem.RenderGlyphOnAtlas(glyph);

			return glyph;
		}

		private DynamicFontGlyph GetDynamicGlyph(int codepoint, bool withoutBitmap)
		{
			var result = GetGlyphInternal(codepoint, withoutBitmap);
			if (result == null && FontSystem.DefaultCharacter != null)
			{
				result = GetGlyphInternal(FontSystem.DefaultCharacter.Value, withoutBitmap);
			}

			return result;
		}

	protected internal override FontGlyph GetGlyph(int codepoint, bool withoutBitmap)
	{
			return GetDynamicGlyph(codepoint, withoutBitmap);
	}

	protected override void PreDraw(string str, out float ascent, out float lineHeight, bool withoutBitmap)
		{
			// Determine ascent and lineHeight from first character
			ascent = 0;
			lineHeight = 0;
			var fontSize = (int)(withoutBitmap ? FontSize : FontSize * RenderFontSizeMultiplicator);
			for (int i = 0; i < str.Length; i += char.IsSurrogatePair(str, i) ? 2 : 1)
			{
				var codepoint = char.ConvertToUtf32(str, i);

				var glyph = GetDynamicGlyph(codepoint, withoutBitmap);
				if (glyph == null)
				{
					continue;
				}

				float descent;
				glyph.Font.GetMetricsForSize(fontSize, out ascent, out descent, out lineHeight);
				lineHeight += FontSystem.LineSpacing;
				break;
			}
		}

		protected override void PreDraw(StringBuilder str, out float ascent, out float lineHeight, bool withoutBitmap)
		{
			// Determine ascent and lineHeight from first character
			ascent = 0;
			lineHeight = 0;
			var fontSize = (int)(withoutBitmap ? FontSize : FontSize * RenderFontSizeMultiplicator);
			for (int i = 0; i < str.Length; i += StringBuilderIsSurrogatePair(str, i) ? 2 : 1)
			{
				var codepoint = StringBuilderConvertToUtf32(str, i);

				var glyph = GetDynamicGlyph(codepoint, withoutBitmap);
				if (glyph == null)
				{
					continue;
				}

				float descent;
				glyph.Font.GetMetricsForSize(fontSize, out ascent, out descent, out lineHeight);
				break;
			}
		}

		public override float TextBounds(string str, Vector2 position, ref Bounds bounds, Vector2 scale)
		{
			if (string.IsNullOrEmpty(str)) return 0.0f;

			var result = base.TextBounds(str, position, ref bounds, scale);

			bounds.X2 += FontSystem.StrokeAmount * 2;

			return result;
		}

		public override float TextBounds(StringBuilder str, Vector2 position, ref Bounds bounds, Vector2 scale)
		{
			if (str == null || str.Length == 0) return 0.0f;

			var result = base.TextBounds(str, position, ref bounds, scale);

			bounds.X2 += FontSystem.StrokeAmount * 2;

			return result;
		}

		internal override void GetQuad(FontGlyph glyph, FontGlyph prevGlyph, Vector2 scale, ref float x, ref float y, ref FontGlyphSquad q)
		{
			if (prevGlyph != null)
			{
				float adv = 0;

				var dynamicGlyph = (DynamicFontGlyph)glyph;
				var dynamicPrevGlyph = (DynamicFontGlyph)prevGlyph;
				if (FontSystem.UseKernings && dynamicGlyph.Font == dynamicPrevGlyph.Font)
				{
					adv = dynamicPrevGlyph.Font.GetGlyphKernAdvance(prevGlyph.Id, dynamicGlyph.Id, dynamicGlyph.Size);
				}

				x += (int)(adv + FontSystem.CharacterSpacing + 0.5f);
			}

			base.GetQuad(glyph, prevGlyph, scale, ref x, ref y, ref q);
		}
	}
}