using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

static class BuildIcon {
    static void Main(string[] args) {
        var images = new List<byte[]>();
        int[] sizes = {16,24,32,48,64,128,256};
        foreach(int size in sizes) {
            using(var bitmap = new Bitmap(size,size,PixelFormat.Format32bppArgb))
            using(var graphics = Graphics.FromImage(bitmap)) {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.ScaleTransform(size/256f,size/256f);
                graphics.Clear(Color.Transparent);
                using(var shape = new GraphicsPath()) {
                    const int r = 46;
                    shape.AddArc(8,8,r*2,r*2,180,90);
                    shape.AddArc(248-r*2,8,r*2,r*2,270,90);
                    shape.AddArc(248-r*2,248-r*2,r*2,r*2,0,90);
                    shape.AddArc(8,248-r*2,r*2,r*2,90,90);
                    shape.CloseFigure();
                    using(var fill = new SolidBrush(Color.FromArgb(24,27,33)))graphics.FillPath(fill,shape);
                    using(var border = new Pen(Color.FromArgb(67,73,83),3))graphics.DrawPath(border,shape);
                }
                using(var blue = new Pen(Color.FromArgb(47,132,255),23))
                using(var white = new Pen(Color.FromArgb(245,247,251),23)) {
                    blue.StartCap=blue.EndCap=white.StartCap=white.EndCap=LineCap.Round;
                    graphics.DrawArc(blue,58,58,140,140,140,180);
                    graphics.DrawArc(white,58,58,140,140,-40,166);
                    graphics.DrawLine(white,128,46,128,120);
                }
                using(var stream = new MemoryStream()) {
                    bitmap.Save(stream,ImageFormat.Png);images.Add(stream.ToArray());
                }
                if(size==256)bitmap.Save(Path.Combine(args[0],"app-icon.png"),ImageFormat.Png);
            }
        }
        using(var writer = new BinaryWriter(File.Create(Path.Combine(args[0],"app.ico")))) {
            writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);
            int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++) {
                writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)(sizes[i]==256?0:sizes[i]));
                writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);
                writer.Write(images[i].Length);writer.Write(offset);offset+=images[i].Length;
            }
            foreach(var png in images)writer.Write(png);
        }
    }
}
