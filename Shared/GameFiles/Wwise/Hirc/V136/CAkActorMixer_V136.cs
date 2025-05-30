﻿using Shared.Core.ByteParsing;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;

namespace Shared.GameFormats.Wwise.Hirc.V136
{
    public class CAkActorMixer_V136 : HircItem, ICAkActorMixer
    {
        public NodeBaseParams_V136 NodeBaseParams { get; set; } = new NodeBaseParams_V136();
        public Children_V136 Children { get; set; } = new Children_V136();

        protected override void ReadData(ByteChunk chunk)
        {
            NodeBaseParams.ReadData(chunk);
            Children.ReadData(chunk);
        }

        public override byte[] WriteData()
        {
            using var memStream = WriteHeader();
            memStream.Write(NodeBaseParams.WriteData());
            memStream.Write(Children.WriteData());
            var byteArray = memStream.ToArray();

            // Reload the object to ensure sanity
            var sanityReload = new CAkActorMixer_V136();
            sanityReload.Parse(new ByteChunk(byteArray));

            return byteArray;
        }

        public override void UpdateSectionSize()
        {
            var idSize = ByteHelper.GetPropertyTypeSize(ID);
            SectionSize = idSize + Children.GetSize() + NodeBaseParams.GetSize();
        }

        public List<uint> GetChildren() => Children.ChildIds;
        public uint GetDirectParentID() => NodeBaseParams.DirectParentID;
    }
}
