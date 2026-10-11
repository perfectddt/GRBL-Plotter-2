using System;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
class SvgStyleTests
{
    static Type svg;
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic;
    static int checks;
    static void Call(string name, params object[] args) { svg.GetMethod(name, Flags).Invoke(null, args); }
    static XElement Document(string body) {
        var root = XElement.Parse("<svg xmlns='http://www.w3.org/2000/svg'>" + body + "</svg>");
        Call("ResetSvgStyle"); Call("CollectCssRules", root); return root;
    }
    static void Check(XElement e, string property, string expected) {
        Call("ResolveSvgStyle", e);
        string actual = (string)e.Attribute(property);
        if (actual != expected) throw new Exception(property + ": expected " + expected + ", actual " + actual);
        checks++;
    }
    static XElement Id(XElement root, string id) {
        foreach (var e in root.Descendants()) if ((string)e.Attribute("id") == id) return e;
        throw new Exception("missing id " + id);
    }
    static int Main(string[] args) {
        try {
            svg = Assembly.LoadFrom(Path.GetFullPath(args[0])).GetType("GrblPlotter.GCodeFromSvg", true);
            var d = Document("<path id='a' stroke='red' fill='blue' stroke-width='3'/><path id='b'/>");
            Check(Id(d,"a"),"stroke","red"); Check(Id(d,"b"),"stroke","none"); Check(Id(d,"b"),"fill","black"); Check(Id(d,"b"),"stroke-width","1");
            d = Document("<style>path{fill:none}.filled{fill:blue}</style><path id='a' class='filled' fill='red'/>");
            Check(Id(d,"a"),"fill","blue");
            d = Document("<style>.filled{fill:blue} #a{fill:green}</style><path id='a' class='filled' style='fill:red'/>");
            Check(Id(d,"a"),"fill","red"); Check(Id(d,"a"),"fill","red");
            d = Document("<style>.x{fill:red}.y{fill:blue}.x{fill:green}</style><path id='a' class='x y'/><path id='b' class='y x'/>");
            Check(Id(d,"a"),"fill","green"); Check(Id(d,"b"),"fill","green");
            d = Document("<g stroke='black' fill='none'><g stroke='red'><path id='a'/></g><path id='b'/></g><path id='c'/>");
            Check(Id(d,"a"),"stroke","red"); Check(Id(d,"b"),"stroke","black"); Check(Id(d,"b"),"fill","none"); Check(Id(d,"c"),"stroke","none");
            d = Document("<style>.scope .x{fill:red} path:hover{fill:blue} .x{fill:green!important}</style><path id='a' class='x' style='fill:red'/><path id='b'/>");
            Check(Id(d,"a"),"fill","green"); Check(Id(d,"b"),"fill","black");
            d = Document("<style>.x{fill:green!important}</style><g color='blue'><path id='a' class='x' style='fill:red!important'/><path id='b' stroke='currentColor'/></g>");
            Check(Id(d,"a"),"fill","red"); Check(Id(d,"b"),"stroke","blue");
            Console.WriteLine("SVG style assertions passed: " + checks); return 0;
        } catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
