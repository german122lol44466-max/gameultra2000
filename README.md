# GameUltra 2000 — фан-игра по мотивам «Звёздных войн»

Модели персонажей с оружием и анимациями плюс тестовый проект с тестовой картой, где всех персонажей
можно посмотреть в движении.

> Это некоммерческий фан-проект. «Звёздные войны», имена персонажей и их дизайн принадлежат Lucasfilm/Disney.
> Для личной игры и с друзьями — нормально; для продажи (Steam и т. п.) нужно будет заменить персонажей на своих.

![Состав](docs/renders/lineup.png)
![Экшен](docs/renders/action.png)

## Что есть

### Персонажи (7)
| Модель | Рост | Оружие | Клипы |
|---|---|---|---|
| Штурмовик (`Stormtrooper`) | 1.83 | бластер E-11 | Idle, Walk, Run, Aim, AimIn, Fire, Reload, Hit, Death |
| Тяжёлый штурмовик (`HeavyTrooper`) — чёрный наплечник, ранец | 1.85 | тяжёлый бластер DLT-19 | те же |
| Командир штурмовиков (`TrooperCommander`) — оранжевый наплечник | 1.83 | E-11 | те же |
| Дарт Вейдер (`DarthVader`) — шлем, маска, пульт на груди, плащ | 2.02 | световой меч (хват двумя руками) | Idle, Ignite, Walk, Run, Attack1-3, Block, ForcePush, **ForceChoke**, Hit, Death |
| Дарт Мол (`DarthMaul`) — татуировки, рожки | 1.75 | двойной меч-посох | то же + Attack3 с вращением посоха |
| Дарт Сидиус (`DarthSidious`) — капюшон, роба | 1.73 | световой меч | то же + **ForceLightning** |
| Граф Дуку (`CountDooku`) — плащ с цепью, седая борода | 1.93 | меч с изогнутой рукоятью (фехтовальная стойка) | то же |

Все персонажи на **одном скелете**: Root, Hips, Spine, Chest, Neck, Head, Shoulder/UpperArm/LowerArm/Hand.L/R,
UpperLeg/LowerLeg/Foot/Toes.L/R + `Weapon.R` (оружие в правой руке), `Blade.R`/`Blade.R2` (клинки: масштаб по Y
— клинок зажжён/погашен, клип Ignite) и `Cape.1-3` (плащ). Анимации запечены по кадрам (30 к/с), на месте, без root motion.
Idle/Walk/Run/Aim зациклены. Walk — 1.6 м/с, Run — 4.5 м/с.

### Оружие (отдельные модели)
E-11, DLT-19, мечи Вейдера, Мола (двойной), Сидиуса, Дуку — `Assets/_Project/Art/Weapons/*.fbx`.

![Оружие](docs/renders/weapons.png)

### Раскадровки анимаций
В `docs/renders/` лежит по картинке на каждый клип каждого персонажа (`<Имя>_<Клип>.png`, 6 кадров в ряд),
плюс портреты (`<Имя>.png`) и крупные планы лиц и шлемов (`<Имя>_face.png`).

## Файлы
```
Assets/_Project/Art/Characters/  <Имя>.fbx (Unity), <Имя>.glb (веб), characters.json — клипы и материалы
Assets/_Project/Art/Weapons/     оружие отдельно
Assets/_Project/Editor/          сборщик тестовой карты для Unity, настройки импорта
Assets/_Project/Scripts/         ShowcaseCharacter, Patrol, FlyCamera, TestMapUI
WebTestMap/                      та же тестовая карта в браузере (three.js, всё лежит локально)
Tools/Blender/                   скрипты, которые строят модели, анимации и рендеры
docs/renders/                    картинки
```

## Тестовый проект в Unity
1. Установите **Unity 6 (6000.0 LTS)**, в Unity Hub нажмите **Add → папка репозитория**.
2. При первом открытии проект сам импортирует модели и соберёт сцену `Assets/_Project/Scenes/TestMap.unity`.
   Если этого не произошло — меню **Star Wars → Собрать тестовую карту**.
3. Нажмите **Play**.

На карте: ангар, ситхи на постаментах впереди, штурмовики на платформе сзади, патрули штурмовиков вокруг,
стол с оружием. Каждый персонаж по очереди проигрывает все свои клипы. Мечи светятся и освещают всё вокруг,
бластер в клипе Fire стреляет болтом, Сидиус в ForceLightning бьёт молниями.

| Управление | |
|---|---|
| ПКМ + мышь | обзор |
| W A S D, Q / E | полёт, вниз / вверх; Shift — быстрее, колесо — скорость |
| Tab | следующий персонаж (камера подлетает к нему) |
| ← / → или 1–9 | клип выбранного персонажа |
| Пробел | вкл/выкл автосмену клипов |
| F1 | скрыть панель |

Проект на встроенном рендер-пайплайне (Built-in, шейдер Standard), материалы создаются по таблице
`characters_unity.json`. Ввод работает и со старым Input Manager, и с новым Input System.

## Тестовая карта в браузере
![Веб-карта](docs/renders/webmap_wide.png)

Тот же ангар и те же модели, без Unity:
- Windows: запустите `WebTestMap/start_server.bat` (нужен Python 3), откроется `http://localhost:8000/WebTestMap/`.
- Linux/macOS: `WebTestMap/start_server.sh`.

ЛКМ — вращать, ПКМ — сдвиг, колесо — зум. Слева список персонажей: клик по имени — камера к персонажу,
клик по клипу — проиграть его.

## Пересборка моделей (Blender)
Нужен Blender 4.2+ или модуль `bpy` (`pip install bpy==4.2.0` под Python 3.11).
```
python Tools/Blender/build_characters.py -- --weapons              # все модели + оружие
python Tools/Blender/build_characters.py -- --only DarthVader --preview --strips   # один персонаж + картинки
python Tools/Blender/render_showcase.py                             # общие рендеры
```
(с Blender: `blender --background --python Tools/Blender/build_characters.py -- ...`)

Как это устроено: модели собираются из примитивов (`swlib/troopers.py`, `swlib/sith.py`, `swlib/weapons.py`).
Анимации задаются ключевыми позами в пространстве персонажа (`swlib/anims.py`): положение оружия, стоп и корпуса.
IK решает руки и ноги (`swlib/rig.py`), затем всё запекается в обычный FK-скелет.
