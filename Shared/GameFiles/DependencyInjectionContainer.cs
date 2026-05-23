using Microsoft.Extensions.DependencyInjection;
using Shared.Core.DependencyInjection;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.Db;

namespace Shared.GameFormats
{
    public class DependencyInjectionContainer : DependencyContainer
    {
        public override void Register(IServiceCollection services)
        {
            services.AddSingleton<IMetaDataDatabase, MetaDataDatabase>();
            services.AddTransient<MetaDataFileParser>();
            services.AddSingleton<IDbSchemaManager, DbSchemaManager>();
            services.AddTransient<IDbTableQueryService, DbTableQueryService>();
        }
    }
}
