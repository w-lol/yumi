using System;
namespace Yumi.Desktop.Editor
{
    // Exact project-specific repair, not a general missing-material fallback.
    // Source FBX was independently inspected: used source materials are EyeBase,
    // Blush and Belts. The first two are donor-face geometry, not the Karin face.
    public static class PantsRepairPolicy
    {
        public const string SourceGuid="39dbdbcf2d331bf4b87123b7d4c6aa67";
        public const string SourceSha256="c19603c1ac975870b0826b5121e7d879804bc374cbd417bfe8182caaa029c05a";
        public const string BeltsGuid="6e00653ed907e0645ad6f0cc4b9e3b37";
        public static bool Matches(string hierarchy,string sourceGuid,string sourceHash,
            string clothingGuid,int materialCount,int submeshCount,bool firstMissing,
            bool secondMissing,bool separateFromFace,bool clothingShaderHealthy)
        {
            return hierarchy=="pants/Body"&&sourceGuid==SourceGuid&&
                String.Equals(sourceHash,SourceSha256,StringComparison.OrdinalIgnoreCase)&&
                clothingGuid==BeltsGuid&&materialCount==3&&submeshCount==3&&
                firstMissing&&secondMissing&&separateFromFace&&clothingShaderHealthy;
        }
    }
}
