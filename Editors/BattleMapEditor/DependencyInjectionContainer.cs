using Editors.BattleMapEditor.Services;
using Editors.BattleMapEditor.Views;
using Editors.BattleMapEditor.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.DependencyInjection;
using Shared.Core.DevConfig;
using Shared.Core.ToolCreation;

namespace Editors.BattleMapEditor
{
    public class DependencyInjectionContainer : DependencyContainer
    {
        public override void Register(IServiceCollection serviceCollection)
        {
            serviceCollection.AddTransient<BattleMapViewerView>();
            serviceCollection.AddScoped<BattleMapViewerViewModel>();
            serviceCollection.AddTransient<BattleMapFolderLoader>();

            RegisterAllAsInterface<IDeveloperConfiguration>(serviceCollection, ServiceLifetime.Transient);
        }

        public override void RegisterTools(IEditorDatabase editorDatabase)
        {
            EditorInfoBuilder
                .Create<BattleMapViewerViewModel, BattleMapViewerView>(EditorEnums.BattleMapViewer_Editor)
                .AddToToolbar("Battle Map Viewer")
                .Build(editorDatabase);
        }
    }
}
