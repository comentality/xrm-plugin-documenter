using System.IO;
using System.Reflection;

namespace PluginStepCodegen.Logic
{
    /// <summary>
    /// The dependency-free subset of XrmTools.Meta.Attributes that this tool can drop
    /// into a project, so emitted attributes compile without the NuGet package or the
    /// Xrm Tools Visual Studio extension.
    /// </summary>
    public static class AttributeDefinitions
    {
        public const string FileName = "XrmToolsMetaAttributes.cs";

        private const string ResourceName = "PluginStepCodegen.XrmToolsMetaAttributes.cs";

        public static string Source
        {
            get
            {
                var assembly = Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream(ResourceName))
                {
                    if (stream == null)
                    {
                        throw new FileNotFoundException("Embedded resource '" + ResourceName + "' is missing.");
                    }

                    using (var reader = new StreamReader(stream))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
        }
    }
}
