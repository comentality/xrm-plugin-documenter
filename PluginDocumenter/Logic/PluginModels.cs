using System;
using System.Collections.Generic;

namespace PluginDocumenter.Logic
{
    public class AssemblyInfo
    {
        public Guid Id;
        public string Name;
        public int IsolationMode;

        /// <summary>
        /// Whether this looks like one of the assemblies Microsoft ships, whose source is in
        /// nobody's folder. No column on the record says so: first party assemblies are managed,
        /// hidden and customization level 1 exactly like anything else that arrived in a solution,
        /// so the name is the only thing left to go on. The UI keeps a switch for when it is wrong.
        /// </summary>
        public bool IsMicrosoft
        {
            get
            {
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
