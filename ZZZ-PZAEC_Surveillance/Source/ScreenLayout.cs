namespace PZAEC.Surveillance
{
    // Coordinates relative to the native model root (block bottom, centered in X/Z).
    // XML ModelOffset=(.5,0,0) cancels the native even-width shift of -.5 on X.
    public static class ScreenLayout
    {
        public const float X=-.5f,Y=1.5f,Z=-.4f;
        public const float Width=3.96f,Height=2.96f,Depth=.18f;
        public const float FaceZ=-.299f,LabelZ=-.284f;
    }
}
