using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GrblPlotter.AutoWriter.Services;
namespace GrblPlotter.AutoWriter
{
    internal sealed class FontDeformationForm:Form
    {
        readonly NumericUpDown[] values=new NumericUpDown[10];
        readonly CheckBox enabled=new CheckBox{Text="启用逐字随机变形",Dock=DockStyle.Fill};
        readonly NumericUpDown seed=new NumericUpDown{Minimum=0,Maximum=int.MaxValue,Dock=DockStyle.Fill};
        readonly Panel preview=new Panel{Dock=DockStyle.Fill,BackColor=Color.White};
        readonly Func<char,Random,BuiltinGlyph> resolve;
        internal FontDeformation Result;
        internal FontDeformationForm(FontDeformation initial,Func<char,Random,BuiltinGlyph> resolve)
        {
            this.resolve=resolve;Text="字体变形";Width=570;Height=650;MinimumSize=new Size(520,620);StartPosition=FormStartPosition.CenterParent;Font=Ui.UiTheme.SafeFont(9f);
            var grid=new TableLayoutPanel{Dock=DockStyle.Top,Height=315,ColumnCount=4,RowCount=7,Padding=new Padding(12)};grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,46));grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,24));grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,25));grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,30));for(int i=0;i<7;i++)grid.RowStyles.Add(new RowStyle(SizeType.Percent,100f/7));grid.Controls.Add(enabled,0,0);grid.SetColumnSpan(enabled,4);
            string[] labels={"随机大小（%）","随机间距（%）","随机倾斜（°）","随机垂直拉伸（%）","随机水平拉伸（%）"};
            for(int row=0;row<5;row++)
            {
                grid.Controls.Add(new Label{Text=labels[row],Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,row+1);grid.Controls.Add(new Label{Text="～",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter},2,row+1);
                for(int j=0;j<2;j++){var field=new NumericUpDown{Dock=DockStyle.Fill,DecimalPlaces=1,Minimum=row==2?-30:10,Maximum=row==2?30:200};values[row*2+j]=field;grid.Controls.Add(field,j==0?1:3,row+1);field.ValueChanged+=(s,e)=>preview.Invalidate();}
            }
            grid.Controls.Add(new Label{Text="随机种子（相同值可复现）",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,6);grid.Controls.Add(seed,1,6);grid.SetColumnSpan(seed,3);
            var buttons=new TableLayoutPanel{Dock=DockStyle.Bottom,Height=42,ColumnCount=4};for(int i=0;i<4;i++)buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));
            buttons.Controls.Add(Button("推荐设置",()=>LoadValues(FontDeformation.Recommended())));buttons.Controls.Add(Button("重置",()=>LoadValues(new FontDeformation())));buttons.Controls.Add(Button("应用",()=>{try{var candidate=ReadValues();candidate.Validate();Result=candidate;DialogResult=DialogResult.OK;}catch(Exception ex){MessageBox.Show(this,ex.Message);}}));buttons.Controls.Add(Button("取消",()=>DialogResult=DialogResult.Cancel));
            Controls.Add(preview);Controls.Add(new Label{Text="在排版前变换每个字；间距比例作用于字距设置。\n超出纸张或行槽会报错，请降低字高或变形幅度。",Dock=DockStyle.Bottom,Height=42});Controls.Add(buttons);Controls.Add(grid);
            enabled.CheckedChanged+=(s,e)=>preview.Invalidate();seed.ValueChanged+=(s,e)=>preview.Invalidate();preview.Paint+=DrawPreview;LoadValues(initial);
        }
        static Button Button(string text,Action action){var b=Ui.UiTheme.MakeButton(text,action,Ui.UiButtonKind.Normal);b.Dock=DockStyle.Fill;return b;}
        void LoadValues(FontDeformation settings)
        {
            double[] source={settings.SizeMin,settings.SizeMax,settings.SpacingMin,settings.SpacingMax,settings.AngleMin,settings.AngleMax,settings.VerticalMin,settings.VerticalMax,settings.HorizontalMin,settings.HorizontalMax};for(int i=0;i<10;i++)values[i].Value=(decimal)source[i];enabled.Checked=settings.Enabled;seed.Value=settings.Seed;preview.Invalidate();
        }
        FontDeformation ReadValues(){return new FontDeformation{Enabled=enabled.Checked,Seed=(int)seed.Value,SizeMin=(double)values[0].Value,SizeMax=(double)values[1].Value,SpacingMin=(double)values[2].Value,SpacingMax=(double)values[3].Value,AngleMin=(double)values[4].Value,AngleMax=(double)values[5].Value,VerticalMin=(double)values[6].Value,VerticalMax=(double)values[7].Value,HorizontalMin=(double)values[8].Value,HorizontalMax=(double)values[9].Value};}
        void DrawPreview(object sender,PaintEventArgs e)
        {
            try
            {
                var settings=ReadValues();settings.Validate();var random=new Random(settings.Seed);var fontRandom=new Random(20261108);double scale=Math.Min(preview.Width/82.0,preview.Height/28.0);int n=0;double cursor=0;e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                foreach(char c in "永和国工王一永和国工王一")
                {
                    if(n%6==0)cursor=0;try{var glyph=settings.Apply(resolve(c,fontRandom),random);foreach(var stroke in glyph.Strokes)for(int i=1;i<stroke.Points.Count;i++){var a=stroke.Points[i-1];var b=stroke.Points[i];e.Graphics.DrawLine(Pens.Black,(float)((cursor+a.X)*scale),(float)((n/6*13+12-a.Y)*scale),(float)((cursor+b.X)*scale),(float)((n/6*13+12-b.Y)*scale));}cursor+=glyph.AdvanceMm+glyph.SpacingScale;}
                    catch(Exception){e.Graphics.DrawString(c+"缺字",Font,Brushes.Firebrick,(float)(n%6*12*scale),(float)(n/6*13*scale+10));}n++;
                }
            }catch(Exception ex){TextRenderer.DrawText(e.Graphics,ex.Message,Font,new Rectangle(8,8,preview.Width-16,preview.Height-16),Color.Firebrick,TextFormatFlags.WordBreak);}
        }
    }
}
