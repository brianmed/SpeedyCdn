# SpeedyCdn

On Premise CDN

Suppports static files and images with a few image operations.

Currently in BETA.

# Usage

## Provision directories and obtain api key

```bash
$ dotnet run -- --originShowApiKey
...
[07:07:38 INF] SpeedyCdn Origin ApiKey: 4392339b-2c5d-48a8-a472-4ca6e23dcd38
```

## Copy some files into the source directory (below is on macOS)

```bash
$ cp *.png ~/.config/SpeedyCdn/OriginSource/Images
```

## Run Origin and Edge on same server with the needed ApiKey

```bash
$ dotnet run -- --edgeOriginApiKey '4392339b-2c5d-48a8-a472-4ca6e23dcd38'
```

# Requirements

Need the MyGet NuGet source defined: &lt;add key="SixLabors" value="https://www.myget.org/F/sixlabors/api/v3/index.json" /&gt;

# Image Operations

The Edge servers support image operations on /v1/images.  Below are some example operations.

?border.Color=Green&border.Thickness=15

?crop.WH=300x300&crop.XY=0,0

?flip.Mode=Horizontal

?label.Text=Tux&label.FontName=Arial&label.FontSize=64&label.TextColor=Yellow&label.OutlineColor=Purple

?resize.WH=500x

?rotate.Mode=Rotate180

## Example Image

Using http://127.0.0.1/v1/images/tux.png?replaceColor.OldColor=Transparent&replaceColor.NewColor=White&label.Text=Tux&label.FontName=Arial&label.FontSize=64&label.OutlineColor=Purple&label.TextColor=Yellow

![Tux with Text](http://bmedley.org/tuxWithText.png)
