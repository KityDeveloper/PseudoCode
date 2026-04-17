# Publicacion

## Release automatico

El workflow `Release` de GitHub Actions se ejecuta manualmente desde **Actions**. El tag es opcional. Si no se escribe uno, el workflow lee la version del proyecto y usa `vX.Y.Z`.

La version actual es `1.0.1`.

## Artefactos

- `PseudoCode-linux-x64-vX.Y.Z.tar.gz`
- `pseudocode_X.Y.Z_amd64.deb`
- `pseudocode-X.Y.Z.x86_64.rpm`
- `PseudoCode-win-x64-vX.Y.Z.zip`

## Instalar en Debian/Ubuntu

```bash
sudo apt install ./pseudocode_X.Y.Z_amd64.deb
pseudocode
```

## Instalar en Fedora

```bash
sudo dnf install ./pseudocode-X.Y.Z.x86_64.rpm
pseudocode
```

## Probar portable en Bazzite

```bash
tar -xzf PseudoCode-linux-x64-vX.Y.Z.tar.gz
./PseudoCode-linux-x64/PseudoCode.App
```

## Publicar manualmente

Linux x64:

```bash
dotnet publish src/PseudoCode.App -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/linux-x64
```

Windows x64:

```bash
dotnet publish src/PseudoCode.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/win-x64
```
