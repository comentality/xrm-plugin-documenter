using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PluginDocumenter.Logic;

namespace PluginDocumenter.UiHarness
{
    /// <summary>
    /// Hosts the tool's control in a bare form, fills it with sample rows and screenshots it, so
    /// layout work can be checked without XrmToolBox and without a Dataverse connection. Run it
    /// through ui.ps1 rather than directly.
    ///
    ///   uiharness.exe &lt;width&gt; &lt;height&gt; &lt;output.png&gt;
    ///
    /// The control is driven the way XrmToolBox drives it - construct, dock, show - so anything
    /// the real tool does on Load and Resize happens here too. Sample data goes in through
    /// reflection: the lists and their backing fields are private, and exposing them for a test
    /// harness would be a worse trade than this file knowing their names.
    /// </summary>
    internal static class Program
    {
        private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("usage: uiharness.exe <width> <height> <output.png>");
                return 2;
            }

            var width = int.Parse(args[0]);
            var height = int.Parse(args[1]);
            var outPath = args[2];

            // Without this a layout mistake surfaces as a modal error dialog on a machine nobody
            // is looking at, and the run just hangs until it is killed.
            var failures = new List<string>();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => failures.Add(e.Exception.ToString());

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var control = new PluginDocumenterControl { Dock = DockStyle.Fill };
            var form = new Form
            {
                Text = "Plugin Documenter UI harness",
                ClientSize = new Size(width, height),
                StartPosition = FormStartPosition.Manual,
                Location = new Point(0, 0),
                TopMost = true      // the screen capture fallback grabs whatever is on top
            };
            form.Controls.Add(control);

            form.Shown += (s, e) =>
            {
                try { Populate(control); }
                catch (Exception ex) { failures.Add(ex.ToString()); }

                // Let the form settle before grabbing it: the lists redistribute their columns on
                // resize, and the preview is debounced and then coloured in a pass of its own.
                var settle = new Timer { Interval = 700 };
                settle.Tick += (s2, e2) =>
                {
                    settle.Stop();
                    try { Capture(form, outPath); }
                    catch (Exception ex) { failures.Add(ex.ToString()); }
                    form.Close();
                };
                settle.Start();
            };

            Application.Run(form);

            if (failures.Count == 0) return 0;

            var log = outPath + ".error.txt";
            File.WriteAllText(log, string.Join("\n----\n", failures));
            Console.Error.WriteLine("failed, see " + log);
            return 1;
        }

        private const uint PW_RENDERFULLCONTENT = 0x00000002;

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        /// <summary>
        /// Asks the window for its own pixels rather than reading them off the screen: scraping the
        /// screen fails outright on a locked workstation, and picks up whatever happens to be in
        /// front otherwise. Screen capture stays as a fallback for anything PrintWindow refuses.
        /// </summary>
        private static void Capture(Form form, string outPath)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using (var bmp = new Bitmap(form.Bounds.Width, form.Bounds.Height))
            {
                var printed = false;
                using (var g = Graphics.FromImage(bmp))
                {
                    var hdc = g.GetHdc();
                    try { printed = PrintWindow(form.Handle, hdc, PW_RENDERFULLCONTENT); }
                    finally { g.ReleaseHdc(hdc); }
                }

                if (!printed)
                    using (var g = Graphics.FromImage(bmp))
                        g.CopyFromScreen(form.Bounds.Location, Point.Empty, form.Bounds.Size);

                bmp.Save(outPath, ImageFormat.Png);
            }
        }

        private static T Field<T>(object target, string name)
        {
            var field = target.GetType().GetField(name, Priv);
            if (field == null) throw new MissingFieldException(target.GetType().Name, name);
            return (T)field.GetValue(target);
        }

        private static void Invoke(object target, string name)
        {
            var method = target.GetType().GetMethod(name, Priv);
            if (method == null) throw new MissingMethodException(target.GetType().Name, name);
            method.Invoke(target, null);
        }

        /// <summary>
        /// Fills the control's own state rather than its lists, then asks it to render: everything
        /// the screenshot is meant to show - the Microsoft count on the switch, the status line,
        /// the grouping, the preview - is computed on the way from one to the other.
        /// </summary>
        private static void Populate(PluginDocumenterControl control)
        {
            var assemblies = SampleAssemblies();
            control.GetType().GetField("_assemblies", Priv).SetValue(control, assemblies);

            var typesByAssembly = Field<Dictionary<Guid, List<PluginTypeInfo>>>(control, "_typesByAssembly");
            var checkedAssemblies = Field<HashSet<Guid>>(control, "_checkedAssemblies");

            // The three that are ticked: one that carries most of the registrations, one small one,
            // and one that turned out to have nothing registered at all - which the list cannot show
            // and the status line has to.
            foreach (var assembly in assemblies.GetRange(0, 3))
            {
                checkedAssemblies.Add(assembly.Id);
                typesByAssembly[assembly.Id] = SampleTypes(assembly);
            }

            // A folder that exists, so the buttons render enabled. It is the harness's own output
            // folder; nothing is written to it.
            Field<TextBox>(control, "_txtFolder").Text = AppDomain.CurrentDomain.BaseDirectory;

            Invoke(control, "RenderAssemblies");
            Invoke(control, "RenderTypes");
        }

        private static List<AssemblyInfo> SampleAssemblies()
        {
            var own = new List<AssemblyInfo>
            {
                NewAssembly("Contoso.Plugins", "a1b2c3d4e5f60718", 2),
                NewAssembly("Contoso.Integration.Plugins", "a1b2c3d4e5f60718", 2),
                // Nothing registered against it, and outside the sandbox besides.
                NewAssembly("Fabrikam.Shared.Plugins", "0f2c9a1b7d3e4655", 1),
                NewAssembly("MsContoso.Extensions", "9d8c7b6a5e4f3021", 2),
                // An ISV's app: neither Microsoft's nor yours, and the reason the Managed switch
                // needs a bucket of its own rather than being read off the signature.
                NewAssembly("Northwind.Suite.Plugins", "4c5d6e7f80912a3b", 2, managed: true),
            };

            // Enough of Microsoft's own to make the point the switch exists for: they outnumber
            // yours by an order of magnitude in any real environment.
            var microsoft = new[]
            {
                "Microsoft.Crm.ObjectModel", "Microsoft.Crm.Extensibility.MessageHandlers",
                "Microsoft.Dynamics.Sales.Plugins", "Microsoft.Dynamics.Sales.OrderProcessing",
                "Microsoft.Dynamics.Sales.Insights.Plugins", "Microsoft.PowerApps.Checker.Plugins",
                "Microsoft.Xrm.Portal.Plugins", "Microsoft.Dynamics.Solutions.AppProfileManager",
                "Microsoft.Dynamics.Field.Service.Plugins", "Microsoft.Crm.ScheduledJobs",
                "Microsoft.Dynamics.CustomerInsights.Plugins", "Microsoft.Dynamics.Forms.Plugins",
            };

            foreach (var name in microsoft)
            {
                own.Add(NewAssembly(name, "31bf3856ad364e35", 2, managed: true));
            }

            return own;
        }

        private static AssemblyInfo NewAssembly(string name, string key, int isolation, bool managed = false)
        {
            return new AssemblyInfo
            {
                Id = Guid.NewGuid(),
                Name = name,
                PublicKeyToken = key,
                IsolationMode = isolation,
                IsManaged = managed
            };
        }

        private static List<PluginTypeInfo> SampleTypes(AssemblyInfo assembly)
        {
            switch (assembly.Name)
            {
                case "Contoso.Plugins":
                    return new List<PluginTypeInfo>
                    {
                        NewType(assembly, "Contoso.Plugins.Accounts.AccountPreValidation", NewStep("Create", "account", 10, 0)),
                        NewType(assembly, "Contoso.Plugins.Accounts.AccountNumberGenerator",
                            NewStep("Create", "account", 20, 0),
                            NewStep("Update", "account", 20, 0, "name,telephone1")),
                        NewType(assembly, "Contoso.Plugins.Contacts.ContactDeduplication",
                            WithImage(NewStep("Create", "contact", 40, 0), "PostImage"),
                            NewStep("Update", "contact", 40, 1, "emailaddress1")),
                        // Registered as a type but with no step against it: nothing to document.
                        NewType(assembly, "Contoso.Plugins.Shared.PluginBase"),
                        NewType(assembly, "Contoso.Plugins.Opportunities.OpportunityCloseAudit",
                            NewStep("Win", "opportunity", 40, 1)),
                    };

                case "Contoso.Integration.Plugins":
                    return new List<PluginTypeInfo>
                    {
                        NewType(assembly, "Contoso.Integration.Plugins.ErpOrderSync",
                            WithImage(NewStep("Update", "salesorder", 40, 1, "statecode"), "PreImage")),
                        NewType(assembly, "Contoso.Integration.Plugins.WebhookRetryHandler",
                            NewStep("contoso_RetryFailedSync", "none", 30, 0)),
                    };

                default:
                    return new List<PluginTypeInfo>();
            }
        }

        private static PluginTypeInfo NewType(AssemblyInfo assembly, string typeName, params PluginStepInfo[] steps)
        {
            return new PluginTypeInfo
            {
                Id = Guid.NewGuid(),
                AssemblyId = assembly.Id,
                TypeName = typeName,
                FriendlyName = typeName,
                Steps = new List<PluginStepInfo>(steps)
            };
        }

        private static PluginStepInfo NewStep(string message, string entity, int stage, int mode, string filter = null)
        {
            return new PluginStepInfo
            {
                Id = Guid.NewGuid(),
                MessageName = message,
                PrimaryEntityName = entity,
                Stage = stage,
                Mode = mode,
                Rank = 1,
                FilteringAttributes = filter,
                Name = "Contoso: " + message + " of " + entity
            };
        }

        private static PluginStepInfo WithImage(PluginStepInfo step, string alias)
        {
            step.Images.Add(new PluginImageInfo
            {
                ImageType = alias == "PreImage" ? 0 : 1,
                EntityAlias = alias,
                Name = alias,
                Attributes = "name,accountid,ownerid"
            });
            return step;
        }
    }
}
