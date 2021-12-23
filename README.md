# Summary

SpeedyCdn is an on-premise CDN that supports Origin and Edge servers. The Origin servers store files used by the Edge servers. Both Origin and Edge can be ran on the same server or different servers. In theory, multiple Edge servers can run and contact the same Origin server (making them act as CDN POPs).

Suppports images, s3 images, static files, and qr codes.

See the wiki for more.

Currently in BETA.

# Provisioning

SpeedyCdn below is an executable downloaded from the Releases page.  The example commands are on Linux.

```bash
# Provision directories, create keys, and display the keys
$ ./SpeedyCdn --originShowKeys
...
Origin ApiKey: ORIGIN_API_KEY
Origin SignatureKey: ORIGIN_SIGNATURE_KEY

# Add some files so they will be served via http by the Edge server
$ cp /abs/path/*.png ~/.config/SpeedyCdn/OriginSource/Images

# Run again with the given api key
$ ./SpeedyCdn --edgeOriginApiKey 'ORIGIN_API_KEY'
```

After the above, all images copied into OriginSource/Images will be available via the Edge, like so.

```bash
$ curl http://IP:8080/v1/images/file.png
```

## Example Image

Using http://127.0.0.1/v1/images/tux.png?replaceColor.OldColor=Transparent&replaceColor.NewColor=White&label.Text=Tux&label.FontName=Arial&label.FontSize=64&label.OutlineColor=Purple&label.TextColor=Yellow

![Tux with Text](http://bmedley.org/tuxWithText.png)

# Notes

Two directory trees are created by default:

  - $HOME/.net
  - $HOME/.config/SpeedyCdn


# Build Requirements

Need the MyGet NuGet source defined: &lt;add key="SixLabors" value="https://www.myget.org/F/sixlabors/api/v3/index.json" /&gt;

Also, a commercial version will be available at some point.
