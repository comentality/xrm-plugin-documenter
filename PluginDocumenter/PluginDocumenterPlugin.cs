using System.ComponentModel.Composition;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace PluginDocumenter
{
    [Export(typeof(IXrmToolBoxPlugin))]
    [ExportMetadata("Name", "Plugin Documenter")]
    [ExportMetadata("Description", "Read registered plugin steps and images from the connected environment and write them into your C# source as Xrm Tools compatible attributes")]
    [ExportMetadata("SmallImageBase64", "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAG1SURBVFhHY2BAA6pRUwyUoqYnKEdOb6A2BpmrEj3NAt1OMABJKEdNu64cNf0/HfBz5YgpHmiWYyiiOQaFCIN8/HwO5ahp99El6YIjp78fMN/DMINSxLQCdEF6YgZwCsUiQS+M1QEWWQv/RzVvJBlrxc/CsIAQxuqAshn7/pMD7POXYFhACFPVAdPb0/5/WKCBgj+i8UH42nQz4hzw4+ev/6cu3SSIcTkAm+UkOeDTl2//py3dQhCjO+DxHP3/nbXx//vrY/5v7vb4v6AlGMzf2+9CmgP+/Pn7/+mLNwQxugOQLUDGIEegy+N1AKmA6g74+u3H/417jhPENHMAuWmAag4gFQw/Bwx4FAx4IiQVUN0BuAqiF6/fodsNBjAH3J1p9D8suxqMUwpK4OzFbUGkOQBXGli8AXsIodcFuDDRDsBVGV28fg/dbjCgugNIBaGlU+DBDcPFpTn/E/LKUcQCMuvxO8CncvX/iWtPk4wNU+ahmEMMxuoAemIGpchpFeiC9MQMoC4SuiA9MYN8fL+AcuS07+gS9MLgvqFS1NQMdAm64Mjp0+EdVHBaoGdIRE6bD+qXAgAfvtffOeYo8wAAAABJRU5ErkJggg==")]
    [ExportMetadata("BigImageBase64", "iVBORw0KGgoAAAANSUhEUgAAAFAAAABQCAYAAACOEfKtAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAN8SURBVHhe7ZyxaxRBFMavtLS0O7lt8mcINkFs1du1iGVKSxtBbSQGTJPsok0EkVgoURRsQsQqQQgSEaMIsZBwghKNilGblTfJevHbvXtzN3PZJPs9+DW7e9/O+3F3M7ksU6tZVn1k+lBwNjkmNKLkXBAmlw4apq/tHqVfdNBXSWgjSmaDMN4MoiStEtJ3I5oaRSdWZd5pYfICQ6tJvBI0J4fRUcdqNOPz+RASRMkEuspVEMbTBS8k2zSi5EnH78dGGF/AF5BCZtBdTT7jBReSDuQmF04YPdOqj0wc3pIXTY0WXEB0rm4LjJ8WnCQq8UpN3oZVXCT7wiyY8SCxR5YuTTxI7OFfHY7UzC8RBSeIHRToCAU6QoGO9CVw/O5iuvh6rXRwXGXQl8B7z96ke6FwXGVAgY7se4FjF0esuXnldDp77UR6f+xkGl9u5s53Ax1k7HuBX28N7QroIIMCLUEHGRRoCTrI8Cbw24+f6fOXbwfC6ocW3s6UjAUbHRToIMObwLWPn9P4zuOBML+wjLczJWPBRjuxUXCsF9BBxoEViLOo8Gh8OJ27ftwgMzGeFzCnsgJxzLZgjpbnTeCn9Y304dzCQFh69Q5vZ0rGgo1qDWtgjpbnTWAZJWPBRrWGNTBHy6NAAHO0PAoEMEfLo0AAc7Q8bwJ//f5jZuJBsL7xHW9nSsaCjWoNa2COludNoDSKyw9fVGIZQ4E9QIFtKBDAHC3Pm0BOIj1QJLCMkrFgo1rDGpij5VEggDlaHgUCmKPlUSCAOVqeN4H8OasHigTKbInLD19UYhlDgT1AgW28Cdxr/9bEMduCOVqeN4FllIwFG9Ua1sAcLY8CAczR8igQwBwtjwIBzNHyvAl0mUSWV1YxzqpkLNhoBj5xINg8H4g5uybQZRlz+8E8xlmVjAUbHRToIIMCLUEHGRRoCTrIoEBL0EGGN4FllIwFG+3Enn8+sIySseAs2g2b5wM7gQ4y9r3AsqFARyjQkb4EkjYU6AgFOkKBjnDHNkfMFp94kNjDnYscqdXPJEfxILGHewe60TICORP3SRhPG4FmC7woaeUuIJ0J4816eGOovQUoNyHrlfyWyGbH8vyFBJA5o3ArZDnICUUjfi8rF3T3r+T7kHuqFmPeed3k7SzZoZZ7q+4gTJLCj223qp+aPGK2hg+TL7nAKrDV98x/sy3UXyeG2xl/+Da6AAAAAElFTkSuQmCC")]
    [ExportMetadata("BackgroundColor", "White")]
    [ExportMetadata("PrimaryFontColor", "#000000")]
    [ExportMetadata("SecondaryFontColor", "#999999")]
    public class PluginDocumenterPlugin : PluginBase
    {
        public override IXrmToolBoxPluginControl GetControl()
        {
            return new PluginDocumenterControl();
        }
    }
}
