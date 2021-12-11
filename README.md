# SpeedyCdn

On Premise CDN

Suppports static files and images with a few image operations.

Currently in BETA.

# Usage

## Provision directories and obtain api key

```bash
$ dotnet run -- --originShowApiKey
...
[07:07:38 INF] SpeedyCdn Origin ApiKey: ORIGIN_API_KEY
```

## Copy some files into the source directory (below is on macOS)

```bash
$ cp *.png ~/.config/SpeedyCdn/OriginSource/Images
```

## Run Origin and Edge on same server with the needed ApiKey

```bash
$ dotnet run -- --edgeOriginApiKey 'ORIGIN_API_KEY'
```

# Requirements

Need the MyGet NuGet source defined: &lt;add key="SixLabors" value="https://www.myget.org/F/sixlabors/api/v3/index.json" /&gt;

# Signatures

Query Parameter and file paths can be restricted with a signature.  The signature is constructed from the SHA-256 hash function and used as a Hash-based Message Authentication Code (HMAC).  This results in tamper free query strings.

```bash
$ curl -v -H 'ApiKey: ORIGIN_API_KEY' 'http://192.168.1.184:8080/v1/signature/create/tux.png?resize.WH=300x'
{"signature":"ce67ada60a0f739b3c9538ce70206118959fc28e331f7f0ba062dc436cdecf90"}

$ curl -v 'http://192.168.1.184:80/v1/images/tux.png?resize.WH=300x&signature=ce67ada60a0f739b3c9538ce70206118959fc28e331f7f0ba062dc436cdecf90' > /dev/null 
...
> GET /v1/images/tux.png?resize.WH=300x&signature=ce67ada60a0f739b3c9538ce70206118959fc28e331f7f0ba062dc436cdecf90 HTTP/1.1
> Host: 192.168.1.184
> User-Agent: curl/7.64.1
> Accept: */*
> 
< HTTP/1.1 200 OK
< Content-Length: 63973
< Content-Type: image/png
< Date: Sat, 11 Dec 2021 21:31:10 GMT
< Server: Kestrel
< 
$ curl -v 'http://192.168.1.184:80/v1/images/tux.png?resize.WH=300x300&signature=ce67ada60a0f739b3c9538ce70206118959fc28e331f7f0ba062dc436cdecf90' > /dev/null
...
> GET /v1/images/tux.png?resize.WH=300x300&signature=ce67ada60a0f739b3c9538ce70206118959fc28e331f7f0ba062dc436cdecf90 HTTP/1.1
> Host: 192.168.1.184
> User-Agent: curl/7.64.1
> Accept: */*
> 
< HTTP/1.1 404 Not Found
< Content-Length: 0
< Date: Sat, 11 Dec 2021 21:31:19 GMT
< Server: Kestrel
< 
```

Note how the change from resize.WH=300x to resize.WH=300x300 caused a 404. 

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

Also, a commercial version will be available at some point.
