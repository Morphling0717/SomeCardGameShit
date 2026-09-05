// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.GodotClient.Battlefield;
using Scgs.GodotClient.CardFaces;

namespace Scgs.GodotClient.PresentationV2;

/// <summary>Render at a stable high font resolution; fit actual outline ink, not CJK line leading.</summary>
internal static class RefinedCardTypography
{
    private const int RasterSize=128;
    private static readonly FontFile Font=CreateFont();
    private readonly record struct RunMetrics(Rect2 Ink,float Ascent,float Width);
    private static readonly Dictionary<string,RunMetrics> Bounds=new(StringComparer.Ordinal);

    internal static void Apply(Label3D label,CardFaceRect socket,string value,bool number,Color? inkColor=null)
    {
        if(string.IsNullOrEmpty(value)){label.Visible=false;label.Text="";return;}
        RunMetrics run=Measure(value);
        Rect2 ink=run.Ink;
        float w=socket.Width*BattlefieldPerspective.CardWidth;
        float h=socket.Height*BattlefieldPerspective.CardWidth/.75f;
        // Numbers and name never borrow decorative territory. The outline is
        // included in the fit, rather than an extra glow outside the socket.
        // One consistent condensed numeral cut across hand/field/detail. Two
        // digits retain the same cap-height as one digit without enlarging a
        // gem or borrowing the name ribbon. Full card names stay uncondensed.
        float numeralWidth=number?.66f:1f;
        label.Scale=new Vector3(numeralWidth,1,1);
        float pixel=Math.Min(w/((ink.Size.X+3)*numeralWidth),h/(ink.Size.Y+3));
        label.Font=Font;label.FontSize=RasterSize;label.PixelSize=pixel;
        label.OutlineSize=1;label.OutlineModulate=new("202238");
        label.Text=value;label.Modulate=inkColor??(number?Colors.White:new Color("f3e8d2"));
        label.HorizontalAlignment=HorizontalAlignment.Left;label.VerticalAlignment=VerticalAlignment.Top;
        label.TextDirection=TextServer.Direction.Ltr;label.Language="zh-CN";
        label.AutowrapMode=TextServer.AutowrapMode.Off;label.Offset=Vector2.Zero;
        label.Width=Math.Max(run.Width+8,ink.Size.X+8);
        label.NoDepthTest=false;label.RenderPriority=4;label.Visible=true;
        // Label3D::_shape uses top baseline y=-ascent, then subtracts each
        // glyph's y_off/outline y. Its AABB is a deferred *line box*, not ink:
        // https://github.com/godotengine/godot/blob/4.7-stable/scene/3d/label_3d.cpp#L531-L567
        // Never read yesterday's AABB or mix it with a padded atlas rectangle.
        Vector2 center=ink.GetCenter();
        Vector3 localInkCenter=new(center.X*pixel,(-run.Ascent-center.Y)*pixel,0);
        Vector3 worldInkCenter=label.Transform.Basis*localInkCenter;
        label.Position=new Vector3((socket.X+socket.Width*.5f-.5f)*BattlefieldPerspective.CardWidth,
            CardFrameMaster.GlyphSurface,(socket.Y+socket.Height*.5f-.5f)*BattlefieldPerspective.CardWidth/.75f)-
            new Vector3(worldInkCenter.X,0,worldInkCenter.Z);
    }

    private static RunMetrics Measure(string value)
    {
        if(Bounds.TryGetValue(value,out var cached))return cached;
        TextServer server=TextServerManager.GetPrimaryInterface();
        Rid shaped=server.CreateShapedText(TextServer.Direction.Ltr,TextServer.Orientation.Horizontal);
        try
        {
            if(!server.ShapedTextAddString(shaped,value,Font.GetRids(),RasterSize,
                    Font.GetOpentypeFeatures(),"zh-CN") || !server.ShapedTextShape(shaped))
                throw new InvalidOperationException("The card font could not shape its complete name/value.");
            Rect2? ink=null;float pen=0;
            foreach(Godot.Collections.Dictionary glyph in server.ShapedTextGetGlyphs(shaped))
            {
                Rid font=glyph["font_rid"].AsRid();
                long size=glyph["font_size"].AsInt64(),index=glyph["index"].AsInt64();
                int repeat=glyph["repeat"].AsInt32();
                float advance=glyph["advance"].AsSingle();
                Vector2 offset=glyph["offset"].AsVector2();
                Rect2? glyphInk=null;
                if(font.IsValid && index!=0)
                {
                    // TextServer already scales MSDF contours to font_size.
                    // Do not apply the 128/96 source-size ratio a second time.
                    var outline=server.FontGetGlyphContours(font,size,index);
                    if(outline.TryGetValue("points",out Variant points) &&
                       outline.TryGetValue("contours",out Variant ends))
                        glyphInk=ContourBounds(points.AsVector3Array(),ends.AsInt32Array());
                }
                for(int copy=0;copy<repeat;copy++)
                {
                    if(glyphInk is {} rect)
                    {
                        rect.Position+=new Vector2(pen,0)+offset;
                        ink=ink?.Merge(rect)??rect;
                    }
                    pen+=advance;
                }
            }
            float ascent=(float)server.ShapedTextGetAscent(shaped);
            float width=(float)server.ShapedTextGetWidth(shaped);
            // The fixed three-card font must provide real contours; silently
            // substituting a line/atlas box would make a false ink-fit claim.
            if(ink is not {Size.X:>0,Size.Y:>0} actual)
                throw new InvalidOperationException("The card font provided no measurable outline ink.");
            var result=new RunMetrics(actual,ascent,width);
            if(Bounds.Count>=256)Bounds.Clear();Bounds[value]=result;return result;
        }
        finally{server.FreeRid(shaped);}
    }

    // FreeType tags: 1=on-curve, 0=quadratic control, 2=cubic control.
    // Control-point AABBs are larger than the painted curve. Solve its extrema
    // so a curved digit such as 0/8 and the middle dot keep their true center.
    private static Rect2? ContourBounds(Vector3[] points,int[] ends)
    {
        Rect2? bounds=null;
        void Include(Vector2 p)=>bounds=bounds?.Expand(p)??new Rect2(p,Vector2.Zero);
        static Vector2 XY(Vector3 p)=>new(p.X,p.Y);
        void Quadratic(Vector2 a,Vector2 b,Vector2 c)
        {
            Include(a);Include(c);
            for(int axis=0;axis<2;axis++)
            {
                float denominator=a[axis]-2*b[axis]+c[axis];
                if(Math.Abs(denominator)<.000001f)continue;
                float t=(a[axis]-b[axis])/denominator;
                if(t>0 && t<1)Include(a*(1-t)*(1-t)+b*2*(1-t)*t+c*t*t);
            }
        }
        void Cubic(Vector2 a,Vector2 b,Vector2 c,Vector2 d)
        {
            Include(a);Include(d);
            void At(float t){if(t>0 && t<1)Include(a*Mathf.Pow(1-t,3)+b*3*Mathf.Pow(1-t,2)*t+c*3*(1-t)*t*t+d*t*t*t);}
            for(int axis=0;axis<2;axis++)
            {
                float qa=-a[axis]+3*b[axis]-3*c[axis]+d[axis];
                float qb=2*(a[axis]-2*b[axis]+c[axis]),qc=b[axis]-a[axis];
                if(Math.Abs(qa)<.000001f){if(Math.Abs(qb)>.000001f)At(-qc/qb);continue;}
                float discriminant=qb*qb-4*qa*qc;
                if(discriminant>=0){float root=Mathf.Sqrt(discriminant);At((-qb+root)/(2*qa));At((-qb-root)/(2*qa));}
            }
        }
        int first=0;
        foreach(int last in ends)
        {
            if(last<first || last>=points.Length)throw new InvalidOperationException("Invalid card glyph contour.");
            int i=first;Vector2 start;
            if((int)points[first].Z==1){start=XY(points[first]);i++;}
            else if((int)points[first].Z==0)
                start=(int)points[last].Z==1?XY(points[last]):(XY(points[first])+XY(points[last]))*.5f;
            else throw new InvalidOperationException("Unsupported card glyph contour start.");
            Vector2 current=start;Include(start);
            while(i<=last)
            {
                Vector2 p=XY(points[i]);int tag=(int)points[i].Z;
                if(tag==1){Include(p);current=p;i++;}
                else if(tag==0)
                {
                    Vector2 next=i==last?start:XY(points[i+1]);
                    int nextTag=i==last?1:(int)points[i+1].Z;
                    if(nextTag is not (0 or 1))throw new InvalidOperationException("Invalid quadratic card glyph contour.");
                    Vector2 end=nextTag==0?(p+next)*.5f:next;
                    Quadratic(current,p,end);current=end;i+=nextTag==0?1:2;
                }
                else if(tag==2 && i+1<=last && (int)points[i+1].Z==2)
                {
                    bool closes=i+2>last;
                    if(!closes && (int)points[i+2].Z!=1)throw new InvalidOperationException("Invalid cubic card glyph contour.");
                    Vector2 end=closes?start:XY(points[i+2]);
                    Cubic(current,p,XY(points[i+1]),end);current=end;i+=closes?2:3;
                }
                else throw new InvalidOperationException("Invalid card glyph control points.");
            }
            first=last+1;
        }
        return bounds;
    }

    private static FontFile CreateFont()
    {
        var font=(FontFile)GD.Load<FontFile>("res://assets/fonts/NotoSerifCJKsc-SemiBold.otf").Duplicate();
        font.MultichannelSignedDistanceField=true; font.MsdfSize=96;
        return font;
    }
}
