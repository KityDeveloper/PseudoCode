# PseudoCode

![PseudoCode](resources/images/logoPseudoCode.png)

> Editor educativo para escribir, ejecutar y aprender pseudocodigo en español.

[![Version](https://img.shields.io/badge/version-1.1.3-blue)](CHANGELOG.md)
[![Estado](https://img.shields.io/badge/estado-estable-brightgreen)](CHANGELOG.md)
[![Plataformas](https://img.shields.io/badge/plataformas-Windows%20%7C%20Linux%20%7C%20macOS-lightgrey)](#descargas)
[![Releases](https://img.shields.io/badge/descargar-releases-brightgreen)](https://github.com/KityDeveloper/PseudoCode/releases/latest)

PseudoCode es una app de escritorio para practicar logica de programacion con pseudocodigo en español. Esta pensada para estudiantes, profesores y personas que quieren probar algoritmos sin configurar un lenguaje de programacion completo.

El dialecto principal sigue el estilo de **PSeInt**, con editor moderno, multiples archivos, diagnosticos, salida, variables, ayuda integrada y configuracion por JSON.

## Descargas rapidas

- [Descargar ultima version](https://github.com/KityDeveloper/PseudoCode/releases/latest)
- [Ver todos los releases](https://github.com/KityDeveloper/PseudoCode/releases)
- [Notas de version](CHANGELOG.md)
- [Documentacion](docs/ayuda.md)

## Capturas

Estas imagenes vienen de `docs/assets/screenshots` y reflejan la interfaz actual.

### Workspace vacio

Pantalla inicial sin archivos abiertos, con accesos para crear un algoritmo o abrir uno existente.

![Workspace vacio](docs/assets/screenshots/empty-workspace-dark.png)

### Editor principal

Editor en modo oscuro con pestañas, explorador, ayuda por temas, salida, diagnosticos, variables y controles de ejecucion.

![Editor principal](docs/assets/screenshots/editor-dark.png)

### Modo claro

La misma experiencia en modo claro, manteniendo los paneles, ayuda y controles legibles.

![Modo claro](docs/assets/screenshots/editor-light.png)

### Multiples archivos y ayuda

Tabs para varios algoritmos, lista de archivos abiertos y panel de ayuda con ejemplos cargables.

![Multiples archivos y ayuda](docs/assets/screenshots/editor-multiples-archivos-ayuda.png)

### Ejecucion viva

Salida actualizandose mientras corre un algoritmo tipo reloj, con variables visibles y botones de pausa/stop.

![Ejecucion viva](docs/assets/screenshots/reloj-ejecucion-viva.png)

### Pausar y continuar

La ejecucion puede pausarse y retomarse sin congelar la app.

![Ejecucion pausada](docs/assets/screenshots/reloj-ejecucion-pausada.png)

### Depuracion paso a paso

Avance por linea con `F10`, resaltado del paso actual, salida y variables inspeccionables.

![Depuracion paso a paso](docs/assets/screenshots/depuracion-paso-a-paso.png)

### Diagnosticos

Problemas agrupados con contador, linea afectada, causa y posible solucion.

![Diagnosticos](docs/assets/screenshots/diagnosticos-problemas.png)

### Autocompletado

Sugerencias de instrucciones del dialecto activo mientras se escribe.

![Autocompletado](docs/assets/screenshots/autocompletado-diagnosticos.png)

### Documentacion integrada

Ayuda Markdown renderizada dentro de la app con indice y ejemplos de codigo.

![Documentacion integrada](docs/assets/screenshots/documentacion-ayuda.png)

### Pseudolenguaje

Referencia del pseudolenguaje con bloques, comentarios, variables y estructuras.

![Pseudolenguaje](docs/assets/screenshots/documentacion-pseudolenguaje.png)

### Temas de codigo

Editor visual de colores de sintaxis con JSON, controles por campo, colores rapidos y vista previa.

![Temas de codigo](docs/assets/screenshots/temas-codigo-dark.png)

### Temas en modo claro

Los temas de sintaxis tambien pueden editarse y previsualizarse con fondo claro.

![Temas en modo claro](docs/assets/screenshots/temas-codigo-light.png)

### Dialectos configurables

Editor de dialectos JSON con campos editables y vista previa de como cambia la sintaxis.

![Dialectos configurables](docs/assets/screenshots/configurar-sintaxis-pseint.png)

### Dialecto en ingles

Ejemplo de dialecto reemplazable basado en PSeInt, pero con palabras reservadas en ingles.

![Dialecto en ingles](docs/assets/screenshots/configurar-sintaxis-english.png)

### Menus principales

Accesos a archivos, ejecucion, configuracion y ayuda desde la barra superior.

![Menu ejecutar](docs/assets/screenshots/menu-ejecutar.png)

![Menu configuracion](docs/assets/screenshots/menu-configuracion.png)

![Menu ayuda](docs/assets/screenshots/menu-ayuda.png)

### Acerca de

Version de la app, logo, autor Kity Dev, animacion del personaje y enlaces oficiales.

![Acerca de PseudoCode](docs/assets/screenshots/acerca-de.png)

## Indice

- [Para que sirve](#para-que-sirve)
- [Funciones principales](#funciones-principales)
- [Descargas](#descargas)
- [Configuracion JSON](#configuracion-json)
- [Documentacion](#documentacion)
- [Estado del proyecto](#estado-del-proyecto)
- [Autor](#autor)

## Para que sirve

PseudoCode ayuda a escribir y ejecutar ejercicios de pseudocodigo mientras se aprende programacion. Puede usarse para:

- Practicar estructuras como `Si`, `Mientras`, `Para` y `Segun`.
- Ver salida y variables mientras se ejecuta un algoritmo.
- Preparar ejemplos para clase.
- Probar ejercicios sin instalar compiladores grandes.
- Experimentar con dialectos de pseudocodigo configurables.

## Funciones principales

- Editor de pseudocodigo en español.
- Dialecto principal estilo PSeInt.
- Ejecucion de algoritmos con salida viva y variables.
- Controles `Play`, `Pausa/Continuar` y `Stop`.
- Depuracion paso a paso con `F10`.
- Diagnosticos y errores subrayados en rojo.
- Tabs con multiples archivos.
- Documentos recientes desde el menu Archivo.
- Paneles redimensionables.
- Lista de archivos abiertos.
- Ayuda rapida integrada.
- Documentacion Markdown renderizada dentro de la app.
- Dialectos JSON reemplazables.
- Colores de sintaxis configurables.
- Menu **Configuracion** para editar settings, temas y dialectos como JSON.
- Modo claro y modo oscuro.

## Descargas

Los paquetes se publican en [GitHub Releases](https://github.com/KityDeveloper/PseudoCode/releases).

| Plataforma | Paquete | Uso |
| --- | --- | --- |
| Windows | `.zip` | Portable |
| Linux | `.tar.gz` | Portable |
| Debian/Ubuntu | `.deb` | Instalacion con `apt` |
| Fedora/Bazzite | `.rpm` | Instalacion con `dnf` o rpm-ostree |
| macOS Intel | `.zip` | Portable, sin firmar/notarizar |
| macOS Apple Silicon | `.zip` | Portable, sin firmar/notarizar |

> Nota: algunos builds pueden no estar firmados todavia. En Windows o macOS puede aparecer una advertencia del sistema operativo.

## Configuracion JSON

PseudoCode puede reemplazar el lenguaje activo mediante JSON. No usa aliases dentro del mismo dialecto: si cambias `Escribir` por `Mostrar`, entonces `Escribir` deja de ser valido en ese dialecto.

Tambien puedes cambiar los colores de sintaxis del editor con JSON. El tema principal por defecto es `dark`.

Lee la guia completa en [Configuracion JSON](docs/configuracion-json.md).

## Documentacion

- [Ayuda general](docs/ayuda.md)
- [Configuracion JSON](docs/configuracion-json.md)
- [Pseudolenguaje](docs/pseudolenguaje.md)
- [Desarrollo](docs/desarrollo.md)
- [Publicacion](docs/publicacion.md)
- [Notas de version](CHANGELOG.md)

La app tambien puede abrir estos documentos desde el menu **Ayuda**.

## Estado del proyecto

Version actual: **1.1.3**

PseudoCode esta en version estable. La app ya permite editar, abrir varios archivos, ejecutar pseudocodigo con salida en vivo, depurar paso a paso y configurar dialectos/colores desde JSON. Todavia hay trabajo planeado para tabla de prueba de escritorio, mas ejemplos y empaquetado mas pulido.

## Autor

Creado por **Kity Dev**.

![Kity Dev](resources/images/iconKityDev.png)

- YouTube: [@KityDev](https://www.youtube.com/@KityDev)
- GitHub: [KityDeveloper](https://github.com/KityDeveloper)
- Web: [kity.dev](https://kity.dev)

## Licencia

Consulta la licencia del repositorio. Si agregas una licencia nueva, enlazala aqui.
