using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace TigEditor
{
	public enum IconKind
	{
		New, Open, Save, Music, Test, Play, Pause, Stop, Follow,
		Block, Spike, Pit, Delete, Finish, Select,
		Gravity, Effect, EffectRise, Settings, Grid, Snap, Undo, Redo, StartFlag
	}

	// Icons are drawn here so the exe carries no images and they follow the
	// screen scale by themselves. Finished ones go into a cache: a background
	// colour change repaints the whole strip.
	public static class Icons
	{
		private static readonly Dictionary<int, Bitmap> cache = new Dictionary<int, Bitmap>();

		public static Bitmap Make(IconKind kind, int size)
		{
			int key = (int)kind * 4096 + size;
			Bitmap bmp;
			if (cache.TryGetValue(key, out bmp)) return bmp;
			bmp = Draw(kind, size);
			cache[key] = bmp;
			return bmp;
		}

		// The background icon is drawn in the colour of the chosen change.
		public static Bitmap MakeBackground(int size, Color color)
		{
			Bitmap bmp = new Bitmap(size, size);
			using (Graphics g = Graphics.FromImage(bmp))
			{
				g.SmoothingMode = SmoothingMode.AntiAlias;
				g.Clear(Color.Transparent);
				float pad = size * 0.15f;
				float inner = size - pad * 2;
				using (Brush grad = new LinearGradientBrush(
						   new RectangleF(pad, pad, inner, inner),
						   Darker(color), color, 90f))
					g.FillRectangle(grad, pad, pad, inner, inner);
				using (Pen pen = new Pen(Color.FromArgb(40, 40, 40), Math.Max(1f, size / 12f)))
					g.DrawRectangle(pen, pad, pad, inner, inner);
			}
			return bmp;
		}

		private static Color Darker(Color c)
		{
			return Color.FromArgb(c.R * 2 / 5, c.G * 2 / 5, c.B * 2 / 5);
		}

		private static Bitmap Draw(IconKind kind, int size)
		{
			Bitmap bmp = new Bitmap(size, size);
			using (Graphics g = Graphics.FromImage(bmp))
			{
				g.SmoothingMode = SmoothingMode.AntiAlias;
				g.Clear(Color.Transparent);
				float pad = size * 0.15f;
				float inner = size - pad * 2;
				using (Brush dark = new SolidBrush(Color.FromArgb(40, 40, 40)))
				using (Pen pen = new Pen(Color.FromArgb(40, 40, 40), Math.Max(1f, size / 12f)))
				{
					switch (kind)
					{
						case IconKind.New:
						{
							// The sheet is narrower than a square, so it is
							// centred instead of starting at the left edge.
							float w = inner * 0.8f;
							float x = pad + (inner - w) / 2f;
							g.DrawRectangle(pen, x, pad, w, inner);
							g.DrawLine(pen, x + w * 0.25f, size / 2f, x + w * 0.75f, size / 2f);
							g.DrawLine(pen, x + w / 2f, size * 0.35f, x + w / 2f, size * 0.65f);
							break;
						}
						case IconKind.Open:
							g.DrawRectangle(pen, pad, pad + inner * 0.2f, inner, inner * 0.7f);
							g.DrawLine(pen, pad, pad + inner * 0.2f, pad + inner * 0.4f, pad);
							break;
						case IconKind.Save:
							g.DrawRectangle(pen, pad, pad, inner, inner);
							g.FillRectangle(dark, pad + inner * 0.25f, pad, inner * 0.5f, inner * 0.35f);
							break;
						case IconKind.Music:
							g.FillEllipse(dark, pad, size * 0.55f, inner * 0.4f, inner * 0.35f);
							g.DrawLine(pen, pad + inner * 0.4f, size * 0.72f, pad + inner * 0.4f, pad);
							g.DrawLine(pen, pad + inner * 0.4f, pad, size - pad, pad + inner * 0.15f);
							break;
						case IconKind.Test:
							g.FillPolygon(dark, new PointF[]
							{
								new PointF(pad, pad), new PointF(size - pad, size / 2f), new PointF(pad, size - pad)
							});
							break;
						case IconKind.Block:
							g.FillRectangle(dark, pad, pad, inner, inner);
							break;
						case IconKind.Spike:
							g.FillPolygon(dark, new PointF[]
							{
								new PointF(pad, size - pad), new PointF(size / 2f, pad), new PointF(size - pad, size - pad)
							});
							break;
						case IconKind.Pit:
							g.FillRectangle(dark, pad, size * 0.55f, inner, inner * 0.3f);
							break;
						case IconKind.Delete:
							g.DrawEllipse(pen, pad, pad, inner, inner);
							g.DrawLine(pen, pad + inner * 0.2f, size - pad - inner * 0.2f,
											size - pad - inner * 0.2f, pad + inner * 0.2f);
							break;
						case IconKind.Finish:
						{
							// The pole and the cloth take three quarters of the
							// width, and the whole shape is centred.
							float poleW = Math.Max(1f, inner * 0.12f);
							float flagW = inner * 0.72f;
							float left = pad + (inner - flagW) / 2f;
							g.FillRectangle(dark, left, pad, poleW, inner);
							g.FillRectangle(dark, left, pad, flagW, inner * 0.45f);
							break;
						}
						case IconKind.Play:
						{
							// Play, pause and stop share one height.
							float h = inner * 0.8f;
							float top = pad + (inner - h) / 2f;
							g.FillPolygon(dark, new PointF[]
							{
								new PointF(pad + inner * 0.18f, top),
								new PointF(size - pad - inner * 0.05f, size / 2f),
								new PointF(pad + inner * 0.18f, top + h)
							});
							break;
						}
						case IconKind.Pause:
						{
							float h = inner * 0.8f;
							float top = pad + (inner - h) / 2f;
							float barW = inner * 0.26f;
							g.FillRectangle(dark, pad + inner * 0.12f, top, barW, h);
							g.FillRectangle(dark, size - pad - inner * 0.12f - barW, top, barW, h);
							break;
						}
						case IconKind.Stop:
						{
							float h = inner * 0.8f;
							float top = pad + (inner - h) / 2f;
							g.FillRectangle(dark, pad + (inner - h) / 2f, top, h, h);
							break;
						}
						case IconKind.StartFlag:
						{
							// The flag: a straight pole on the right and a
							// triangular cloth to its left, half the pole high.
							// The height matches play, pause and stop.
							float h = inner * 0.8f;
							float top = pad + (inner - h) / 2f;
							float poleW = Math.Max(2f, inner * 0.15f);
							float poleX = pad + inner * 0.60f;
							g.FillRectangle(dark, poleX, top, poleW, h);
							// The right edge of the cloth overlaps the pole, or
							// antialiasing shows a seam between them.
							g.FillPolygon(dark, new PointF[]
							{
								new PointF(poleX + poleW * 0.5f, top),
								new PointF(poleX + poleW * 0.5f, top + h * 0.5f),
								new PointF(pad + inner * 0.06f, top + h * 0.25f)
							});
							break;
						}
						case IconKind.Follow:
						{
							// A simple camera: an almost square body with a wedge
							// of a lens on the side and a raised corner.
							float bodyY = pad + inner * 0.16f;
							float bodyH = inner * 0.68f;
							float bodyW = bodyH;
							g.FillRectangle(dark, pad, bodyY, bodyW, bodyH);
							g.FillPolygon(dark, new PointF[]
							{
								new PointF(pad + bodyW + inner * 0.06f, bodyY + bodyH * 0.3f),
								new PointF(size - pad, bodyY + bodyH * 0.06f),
								new PointF(size - pad, bodyY + bodyH * 0.94f),
								new PointF(pad + bodyW + inner * 0.06f, bodyY + bodyH * 0.7f)
							});
							break;
						}
						case IconKind.Select:
							using (Pen dash = new Pen(Color.FromArgb(40, 40, 40), Math.Max(1f, size / 14f)))
							{
								dash.DashStyle = DashStyle.Dash;
								g.DrawRectangle(dash, pad, pad, inner, inner);
							}
							break;
						case IconKind.Grid:
							for (int i = 0; i <= 3; i++)
							{
								float t = pad + inner * i / 3f;
								g.DrawLine(pen, t, pad, t, size - pad);
								g.DrawLine(pen, pad, t, size - pad, t);
							}
							break;
						case IconKind.Gravity:
						{
							// Gravity flips, so the sign is two sided: arrows
							// reach the line from above and below, meaning the
							// pull can come either way.
							float mid = pad + inner / 2f;
							float head = inner * 0.26f;

							g.FillPolygon(dark, new PointF[]
							{
								new PointF(size / 2f, mid - inner * 0.1f),
								new PointF(size / 2f - head, mid - inner * 0.1f - head),
								new PointF(size / 2f + head, mid - inner * 0.1f - head)
							});
							g.DrawLine(pen, size / 2f, pad, size / 2f, mid - inner * 0.1f - head);

							g.FillPolygon(dark, new PointF[]
							{
								new PointF(size / 2f, mid + inner * 0.1f),
								new PointF(size / 2f - head, mid + inner * 0.1f + head),
								new PointF(size / 2f + head, mid + inner * 0.1f + head)
							});
							g.DrawLine(pen, size / 2f, mid + inner * 0.1f + head, size / 2f, size - pad);

							g.FillRectangle(dark, pad, mid - inner * 0.05f, inner, inner * 0.1f);
							break;
						}
						case IconKind.Effect:
						case IconKind.EffectRise:
						{
							// The dashes are the stripe itself: four dots, the
							// outer ones at the very edges. A block and an arrow
							// of one width sit above and below with equal gaps,
							// aligned to an end of the dashes: the left one for
							// sinking, which happens behind the player, the right
							// one for rising, which happens ahead. The arrow is
							// shorter than the block, so the dashes end up off
							// centre.
							bool up = kind == IconKind.EffectRise;

							float dot = Math.Max(1f, inner * 0.14f);
							float step = (inner - dot) / 3f;

							// The block and the arrow are exactly two dashes wide
							// including the gap between them.
							float box = step + dot;
							float arrow = box / 2f;
							float gap = (inner - box - arrow) / 2f;

							float left = up ? size - pad - box : pad;
							float cx = left + box / 2f;
							float dotsY = up ? pad + arrow + gap : pad + box + gap;
							for (int i = 0; i < 4; i++)
								g.FillRectangle(dark, pad + i * step, dotsY - dot / 2f, dot, dot);

							g.FillRectangle(dark, left, up ? dotsY + gap : pad, box, box);

							float baseY = up ? dotsY - gap : dotsY + gap;
							float tipY = up ? pad : size - pad;
							g.FillPolygon(dark, new PointF[]
							{
								new PointF(cx, tipY),
								new PointF(left, baseY),
								new PointF(left + box, baseY)
							});
							break;
						}
						case IconKind.Settings:
							g.DrawEllipse(pen, pad + inner * 0.25f, pad + inner * 0.25f, inner * 0.5f, inner * 0.5f);
							for (int i = 0; i < 8; i++)
							{
								double ang = i * Math.PI / 4;
								float cx = size / 2f, cy = size / 2f;
								g.DrawLine(pen,
									cx + (float)Math.Cos(ang) * inner * 0.32f,
									cy + (float)Math.Sin(ang) * inner * 0.32f,
									cx + (float)Math.Cos(ang) * inner * 0.5f,
									cy + (float)Math.Sin(ang) * inner * 0.5f);
							}
							break;
						case IconKind.Snap:
							for (int i = 0; i <= 2; i++)
							{
								float t = pad + inner * i / 2f;
								g.DrawLine(pen, t, pad, t, size - pad);
								g.DrawLine(pen, pad, t, size - pad, t);
							}
							g.FillRectangle(dark, pad + inner * 0.5f - size * 0.09f,
											pad + inner * 0.5f - size * 0.09f, size * 0.18f, size * 0.18f);
							break;
						case IconKind.Undo:
						case IconKind.Redo:
							GraphicsPath path = new GraphicsPath();
							path.AddArc(pad, pad + inner * 0.15f, inner, inner * 0.8f, 200, 140);
							if (kind == IconKind.Redo)
							{
								Matrix m = new Matrix();
								m.Scale(-1, 1);
								m.Translate(-size, 0);
								path.Transform(m);
								m.Dispose();
							}
							g.DrawPath(pen, path);
							path.Dispose();
							break;
					}
				}
			}
			return bmp;
		}
	}
}
