using System;
using System.Collections.Generic;
using System.Linq;

namespace PluginStepCodegen.Logic
{
    public class AssemblyInfo
    {
        public Guid Id;
        public string Name;
        public string PublicKeyToken;
        public int IsolationMode;

        /// <summary>
        /// Whether this arrived inside a managed solution. The tool is pointed at a plugin somebody
        /// is in the middle of writing - built, registered straight into a development environment,
        /// source open in the editor - and that registration is unmanaged. Managed means it was
        /// shipped by somebody: Microsoft, an ISV, or you, in a build that is no longer the one on
        /// disk. It is a hint about whose source this is, never a prohibition; nothing here writes
        /// to the environment, so there is no operation a managed assembly would refuse.
        /// </summary>
        public bool IsManaged;

        /// <summary>
        /// Microsoft's strong name keys. Plugin assemblies must be signed, and these are keys
        /// nobody outside Microsoft can sign with, so a match is proof rather than a guess.
        /// </summary>
        private static readonly string[] MicrosoftKeys =
        {
            "31bf3856ad364e35", "b77a5c561934e089", "b03f5f7f11d50a3a", "71e9bce111e9429c"
        };

        /// <summary>
        /// Whether this is one of the assemblies Microsoft ships, whose source is in nobody's
        /// folder. No column on the record says so — first party assemblies are managed and
        /// customization level 1 exactly like anything else that arrived in a solution — and the
        /// name will not answer it either: the optional apps ship under names of their own, and
        /// under several different Microsoft publishers. What they have in common is the
        /// signature, which is why that is asked first and the name is only a second look.
        /// The UI keeps a switch for when both are wrong.
        /// </summary>
        public bool IsMicrosoft
        {
            get
            {
                if (PublicKeyToken != null
                    && MicrosoftKeys.Contains(PublicKeyToken.Trim().ToLowerInvariant()))
                {
                    return true;
                }

                return Name != null && Name.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase);
            }
        }

        public override string ToString()
        {
            return Name;
        }
    }

    public class PluginTypeInfo
    {
        public Guid Id;
        /// <summary>The assembly this type was registered from, for grouping a batch that spans several.</summary>
        public Guid AssemblyId;
        /// <summary>Namespace qualified type name as registered in Dataverse.</summary>
        public string TypeName;
        public string FriendlyName;
        public string Description;
        public List<PluginStepInfo> Steps = new List<PluginStepInfo>();

        /// <summary>Trailing segment of <see cref="TypeName"/>, used to locate the .cs file.</summary>
        public string ClassName
        {
            get
            {
                if (string.IsNullOrEmpty(TypeName))
                {
                    return string.Empty;
                }

                var i = TypeName.LastIndexOf('.');
                return i < 0 ? TypeName : TypeName.Substring(i + 1);
            }
        }
    }

    public class PluginStepInfo
    {
        public Guid Id;
        public string MessageName;
        public string PrimaryEntityName;
        public string FilteringAttributes;
        public int Stage;
        public int Mode;
        public int Rank = 1;
        public string Name;
        public string Description;
        public string Configuration;
        public bool AsyncAutoDelete;
        /// <summary>statecode 1. Documented in the comment only; no attribute can carry it.</summary>
        public bool IsDisabled;
        /// <summary>Full name behind impersonatinguserid, empty when the step runs as the calling user.</summary>
        public string ImpersonatingUser;
        public List<PluginImageInfo> Images = new List<PluginImageInfo>();
    }

    public class PluginImageInfo
    {
        public int ImageType;
        public string EntityAlias;
        public string Attributes;
        public string Name;
        public string MessagePropertyName;
    }
}
