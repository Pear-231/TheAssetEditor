using Shared.Core.ErrorHandling.Exceptions;

namespace Shared.Core.PackFiles.ErrorHandling
{
    class PackFileExceptionInformationProvider : IExceptionInformationProvider
    {
        private readonly IPackFileService _pfs;

        public PackFileExceptionInformationProvider(IPackFileService pfs)
        {
            ;
            _pfs = pfs;
        }

        public void HydrateExcetion(ExceptionInformation exceptionInformation)
        {
            var packfiles = _pfs.GetAllPackfileContainers();
            foreach (var db in packfiles)
            {
                var isActive = _pfs.GetActivePack() == db;
                var info = new ExceptionPackFileContainerInfo(isActive, db.IsCaPackFile, db.Name, db.SystemFilePath);
                exceptionInformation.ActivePackFiles.Add(info);
            }

        }
    }
}
