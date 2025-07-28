﻿using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Editors.Audio.AudioEditor.Events;

namespace Editors.Audio.AudioEditor.UICommands
{
    public class OpenMovieFileSelectionWindowCommand(IStandardDialogs standardDialogs, IEventHub eventHub, IPackFileService packFileService) : IUiCommand
    {
        private readonly IStandardDialogs _standardDialogs = standardDialogs;
        private readonly IEventHub _eventHub = eventHub;
        private readonly IPackFileService _packFileService = packFileService;

        public void Execute()
        {
            var result = _standardDialogs.DisplayBrowseDialog([".ca_vp8"]);
            if (result.Result)
            {
                var movieFilePath = _packFileService.GetFullPath(result.File);
                _eventHub.Publish(new MovieFileChangedEvent(movieFilePath));
            }
        }
    }
}
