// ЭХО: Кассиопея-9 — данные карты, персонажей и сюжета
'use strict';

const TILE = 48;
const MAP_W = 64, MAP_H = 44;

const ROOMS = [
  { id: 'reactor',    name: 'Реактор',        x: 2,  y: 16, w: 9,  h: 12, floor: '#3b2c30', accent: '#ff7a59' },
  { id: 'engine',     name: 'Двигатели',      x: 13, y: 30, w: 10, h: 8,  floor: '#33302a', accent: '#ffb547' },
  { id: 'medbay',     name: 'Медотсек',       x: 14, y: 4,  w: 9,  h: 8,  floor: '#26363a', accent: '#5ff2c2' },
  { id: 'mess',       name: 'Кают-компания',  x: 26, y: 3,  w: 14, h: 11, floor: '#2d3142', accent: '#8fa8ff' },
  { id: 'lab',        name: 'Лаборатория',    x: 26, y: 18, w: 12, h: 9,  floor: '#2e2a40', accent: '#c58bff' },
  { id: 'storage',    name: 'Склад',          x: 26, y: 31, w: 12, h: 10, floor: '#33312b', accent: '#e8d36a' },
  { id: 'greenhouse', name: 'Оранжерея',      x: 44, y: 3,  w: 10, h: 9,  floor: '#26372b', accent: '#7be07b' },
  { id: 'comms',      name: 'Связь',          x: 42, y: 30, w: 10, h: 8,  floor: '#2a3440', accent: '#59c7ff' },
  { id: 'bridge',     name: 'Мостик',         x: 55, y: 16, w: 8,  h: 11, floor: '#2b3348', accent: '#9fd8ff' },
];

const CORRIDORS = [
  [23, 6, 3, 3],   // медотсек — кают-компания
  [40, 6, 4, 3],   // кают-компания — оранжерея
  [6, 8, 3, 8],    // реактор вверх
  [6, 8, 8, 3],    // — к медотсеку
  [6, 28, 3, 6],   // реактор вниз
  [6, 32, 7, 3],   // — к двигателям
  [23, 33, 3, 3],  // двигатели — склад
  [38, 33, 4, 3],  // склад — связь
  [31, 14, 3, 4],  // кают-компания — лаборатория
  [31, 27, 3, 4],  // лаборатория — склад
  [50, 12, 3, 11], // оранжерея вниз
  [53, 19, 2, 3],  // — к мостику
  [47, 23, 3, 7],  // связь вверх
  [47, 23, 8, 3],  // — к мостику
  [38, 22, 12, 3], // лаборатория — восточный коридор
  [11, 21, 15, 3], // реактор — лаборатория (центральный коридор)
];

// Препятствия: мешают ходьбе, но не закрывают обзор
const PROPS = [
  { x: 5, y: 20, w: 3, h: 3, type: 'core' },
  { x: 16, y: 32, w: 3, h: 2, type: 'engine' },
  { x: 32, y: 7, w: 2, h: 2, type: 'button' },
  { x: 28, y: 10, w: 2, h: 2, type: 'table' },
  { x: 36, y: 10, w: 2, h: 2, type: 'table' },
  { x: 30, y: 21, w: 5, h: 2, type: 'bench' },
  { x: 29, y: 36, w: 2, h: 2, type: 'crate' },
  { x: 33, y: 35, w: 2, h: 2, type: 'crate' },
  { x: 35, y: 37, w: 1, h: 1, type: 'crate' },
  { x: 47, y: 5, w: 2, h: 2, type: 'plant' },
  { x: 50, y: 7, w: 2, h: 2, type: 'plant' },
  { x: 59, y: 17, w: 3, h: 1, type: 'console' },
  { x: 45, y: 33, w: 2, h: 2, type: 'dish' },
  { x: 19, y: 5, w: 2, h: 1, type: 'bed' },
];

// Станции заданий (координаты в клетках)
const STATIONS = [
  { id: 'core',     room: 'reactor',    x: 3,  y: 17, type: 'timing',   name: 'Стабилизировать ядро',   time: 5 },
  { id: 'fuel',     room: 'engine',     x: 21, y: 31, type: 'hold',     name: 'Заправить двигатель',    time: 5 },
  { id: 'scan',     room: 'medbay',     x: 17, y: 8,  type: 'scan',     name: 'Пройти биоскан',         time: 6, visual: true },
  { id: 'wires',    room: 'mess',       x: 27, y: 4,  type: 'wires',    name: 'Починить проводку',      time: 4 },
  { id: 'code',     room: 'lab',        x: 27, y: 19, type: 'sequence', name: 'Ввести код доступа',     time: 4 },
  { id: 'crystal',  room: 'lab',        x: 36, y: 19, type: 'download', name: 'Анализ кристалла',       time: 6 },
  { id: 'cargo',    room: 'storage',    x: 36, y: 39, type: 'hold',     name: 'Разгрузить контейнеры',  time: 5 },
  { id: 'spores',   room: 'greenhouse', x: 52, y: 4,  type: 'samples',  name: 'Собрать образцы спор',   time: 5 },
  { id: 'antenna',  room: 'comms',      x: 50, y: 31, type: 'wave',     name: 'Настроить антенну',      time: 5 },
  { id: 'course',   room: 'bridge',     x: 61, y: 24, type: 'download', name: 'Проложить курс',         time: 6 },
];

const TASKS_PER_PLAYER = 5;

// Точки устранения саботажа
const FIXES = {
  lights:  { x: 27, y: 32, room: 'storage', type: 'switches',  name: 'Электрощиток' },
  reactor: { x: 9,  y: 26, room: 'reactor', type: 'handprint', name: 'Аварийный контур' },
  comms:   { x: 43, y: 36, room: 'comms',   type: 'wave',      name: 'Пульт связи' },
};

const SABOTAGE_INFO = {
  lights:  { name: 'Отключить свет',        alert: 'СВЕТ ОТКЛЮЧЁН — почините электрощиток на складе' },
  reactor: { name: 'Перегрузить реактор',   alert: 'ПЕРЕГРУЗКА РЕАКТОРА' },
  comms:   { name: 'Заглушить связь',       alert: 'СВЯЗЬ ПОТЕРЯНА — задания скрыты' },
};

// Вентиляция: в пределах сети можно перемещаться
const VENTS = [
  { id: 0, x: 9,  y: 18, net: 'A', room: 'reactor' },
  { id: 1, x: 15, y: 10, net: 'A', room: 'medbay' },
  { id: 2, x: 14, y: 36, net: 'A', room: 'engine' },
  { id: 3, x: 38, y: 12, net: 'B', room: 'mess' },
  { id: 4, x: 45, y: 10, net: 'B', room: 'greenhouse' },
  { id: 5, x: 56, y: 25, net: 'B', room: 'bridge' },
  { id: 6, x: 36, y: 25, net: 'C', room: 'lab' },
  { id: 7, x: 30, y: 33, net: 'C', room: 'storage' },
  { id: 8, x: 49, y: 36, net: 'C', room: 'comms' },
];

const BUTTON = { x: 32.5, y: 7.5, room: 'mess' }; // центр пульта (2x2 клетки)

const SPAWNS = (() => {
  const s = [];
  for (let i = 0; i < 10; i++) {
    const a = (i / 10) * Math.PI * 2;
    s.push({ x: 33 + Math.cos(a) * 3.2, y: 8 + Math.sin(a) * 2.6 });
  }
  return s;
})();

const COLORS = [
  { id: 'red',    name: 'Алый',       body: '#e5484d', dark: '#9c2a2f' },
  { id: 'blue',   name: 'Кобальт',    body: '#3e63dd', dark: '#25398a' },
  { id: 'green',  name: 'Мята',       body: '#30a46c', dark: '#1b6b44' },
  { id: 'yellow', name: 'Янтарь',     body: '#f5c518', dark: '#a8850c' },
  { id: 'orange', name: 'Апельсин',   body: '#f76b15', dark: '#a8460b' },
  { id: 'purple', name: 'Аметист',    body: '#8e4ec6', dark: '#5b2f82' },
  { id: 'cyan',   name: 'Лёд',        body: '#29c7d9', dark: '#17808c' },
  { id: 'pink',   name: 'Коралл',     body: '#f06aa8', dark: '#a6406f' },
  { id: 'white',  name: 'Снег',       body: '#e8ecf2', dark: '#9aa3b2' },
  { id: 'brown',  name: 'Охра',       body: '#a1704a', dark: '#64432b' },
];

const BOT_NAMES = ['Вега', 'Орион', 'Лира', 'Сириус', 'Альтаир', 'Мира', 'Денеб', 'Ригель', 'Капелла', 'Поллукс', 'Антарес', 'Электра'];

const STORY = {
  title: 'ЭХО',
  subtitle: 'КАССИОПЕЯ-9',
  prologue: [
    '2189 год. Добывающая станция «Кассиопея-9» висит на орбите газового гиганта Тифон, в трёх месяцах пути от Земли.',
    'Неделю назад буровой зонд поднял из облаков странный кристалл. Учёные назвали его «Эхо» — он тихо звенел, повторяя каждый звук, который слышал.',
    'Потом он начал повторять людей.',
    'Эхо копирует человека целиком: голос, походку, привычки, воспоминания. А оригинал превращается в груду звенящих кристаллов.',
    'Связь с Землёй оборвалась. Среди смотрителей станции уже есть копии — и никто не знает, кто именно.',
    'Смотрители должны починить станцию и отправить сигнал бедствия, вычисляя Эхо на общих собраниях.',
    'Эхо должны остаться незамеченными, пока на станции не останется никого настоящего. Тогда спасательный корабль прилетит… и увезёт их на Землю.',
  ],
  crewIntro: 'Ты — Смотритель. Выполни задания, найди Эхо и изгони их со станции.',
  echoIntro: 'Ты — Эхо. Притворяйся человеком, кристаллизуй смотрителей и не дай им отправить сигнал.',
  endings: {
    crew_tasks: ['СИГНАЛ ОТПРАВЛЕН', 'Антенна «Кассиопеи» пробила помехи Тифона. Через три месяца прилетит корабль карантинной службы — с огнемётами и сканерами. Эхо больше не смогут прятаться.'],
    crew_eject: ['ЭХО ИЗГНАНЫ', 'Последняя копия улетела в шлюз и рассыпалась звенящей пылью. На станции снова тихо. Только иногда кажется, что стены повторяют твои шаги…'],
    echo_kill:  ['ЭХО ПОБЕДИЛИ', 'Последний смотритель затих. Копии улыбаются друг другу его улыбкой. Спасательный корабль уже в пути — и он отвезёт их домой. На Землю.'],
    echo_reactor: ['РЕАКТОР ВЗОРВАЛСЯ', 'Вспышка была видна даже с Земли. Эхо не боятся огня — кристаллы разлетелись по орбите Тифона. Однажды их найдёт следующий зонд.'],
  },
};

function shuffle(a) {
  for (let i = a.length - 1; i > 0; i--) {
    const j = (Math.random() * (i + 1)) | 0;
    [a[i], a[j]] = [a[j], a[i]];
  }
  return a;
}
function rand(a, b) { return a + Math.random() * (b - a); }
function pick(a) { return a[(Math.random() * a.length) | 0]; }
function clamp(v, a, b) { return v < a ? a : v > b ? b : v; }
function dist(ax, ay, bx, by) { return Math.hypot(ax - bx, ay - by); }
function roomById(id) { return ROOMS.find(r => r.id === id); }
