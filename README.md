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

## Process Images

Using http://127.0.0.1/v1/images/tux.png?label.Text=Tux&label.FontName=Arial&label.FontSize=64&label.OutlineColor=Purple&label.TextColor=Yellow

![Tux with Text](http://bmedley.org/tuxWithText.png)
