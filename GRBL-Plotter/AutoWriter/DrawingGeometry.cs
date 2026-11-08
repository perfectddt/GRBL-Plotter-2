using System;
using System.Collections.Generic;
using System.Drawing;
namespace GrblPlotter.AutoWriter
{
    public static class DrawingGeometry
    {
        public static bool Contains(IList<PointF> polygon,PointF p)
        {
            bool inside=false;
            for(int i=0,j=polygon.Count-1;i<polygon.Count;j=i++)
            {
                var a=polygon[i];
                var b=polygon[j];
                if((a.Y>p.Y)!=(b.Y>p.Y)&&p.X<(b.X-a.X)*(p.Y-a.Y)/(b.Y-a.Y)+a.X)inside=!inside;
            }
            return inside;
        }
        public static List<double[]> Erase(IEnumerable<double[]> paths,PointF center,double radius)
        {
            if(radius<=0||double.IsNaN(radius)||double.IsInfinity(radius))throw new ArgumentException("橡皮擦半径无效。");
            var output=new List<double[]>();
            foreach(var path in paths)
            {
                var piece=new List<double>();
                Action finish=()=>
                {
                    if(piece.Count>=4)output.Add(piece.ToArray());
                    piece.Clear();
                }
                ;
                for(int i=2;i<path.Length;i+=2)
                {
                    double x=path[i-2],y=path[i-1],dx=path[i]-x,dy=path[i+1]-y,ox=x-center.X,oy=y-center.Y;
                    double a=dx*dx+dy*dy,b=2*(ox*dx+oy*dy),c=ox*ox+oy*oy-radius*radius;
                    var ts=new List<double>
                    {
                        0,1
                    }
                    ;
                    double det=b*b-4*a*c;
                    if(a>1e-18&&det>0)
                    {
                        double root=Math.Sqrt(det);
                        double t1=(-b-root)/(2*a),t2=(-b+root)/(2*a);
                        if(t1>0&&t1<1)ts.Add(t1);
                        if(t2>0&&t2<1)ts.Add(t2);
                    }
                    ts.Sort();
                    for(int k=1;k<ts.Count;k++)
                    {
                        double l=ts[k-1],r=ts[k],mid=(l+r)/2;
                        double cx=ox+dx*mid,cy=oy+dy*mid;
                        if(cx*cx+cy*cy<radius*radius-1e-12)
                        {
                            finish();
                            continue;
                        }
                        double sx=x+dx*l,sy=y+dy*l,ex=x+dx*r,ey=y+dy*r;
                        if((ex-sx)*(ex-sx)+(ey-sy)*(ey-sy)<1e-18)continue;
                        if(piece.Count==0)
                        {
                            piece.Add(sx);
                            piece.Add(sy);
                        }
                        piece.Add(ex);
                        piece.Add(ey);
                    }
                }
                finish();
            }
            return output;
        }
    }
}
