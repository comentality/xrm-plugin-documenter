using System;
using System.Collections.Generic;

namespace PluginDocumenter.Logic
{
    public class AssemblyInfo
    {
        public Guid Id;
        public string Name;
        public int IsolationMode;

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
