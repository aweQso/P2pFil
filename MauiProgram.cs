using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;

namespace P2PFil
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit() // Noktalı virgülü kaldırdık, zincir devam ediyor
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
            builder.Logging.AddDebug();
#endif

            // Sayfayı sisteme kaydediyoruz
            builder.Services.AddTransient<FilesPage>();

            return builder.Build();
        }
    }
}