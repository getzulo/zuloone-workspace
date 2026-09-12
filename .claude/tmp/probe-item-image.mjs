const API = 'http://localhost:5257';
const login = await fetch(`${API}/api/auth/login`, {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ name: 'reacttest', password: 'Test1234!' }),
});
const { token } = await login.json();
const h = { Authorization: `Bearer ${token}` };

const dicts = await (await fetch(`${API}/api/metadata/dictionaries`, { headers: h })).json();
const item = dicts.find((d) => d.name === 'Item');
const fields = await (await fetch(`${API}/api/metadata/dictionaries/${item.metaId}/fields`, { headers: h })).json();
const imageField = fields.find((f) => f.fieldName === 'Image' || f.name === 'Image');
console.log('image field', JSON.stringify(imageField, null, 2));

const list = await (await fetch(`${API}/api/data/Item?take=2`, { headers: h })).json();
const gadget = list.data.find((r) => r.Name === 'Gadget' || r.ID === '1001' || r.ID === 1001);
console.log('gadget keys', gadget && Object.keys(gadget));
const img = gadget?.Image;
console.log('Image type', img === null ? 'null' : Array.isArray(img) ? `array[${img.length}]` : typeof img);
if (typeof img === 'string') console.log('Image prefix', img.slice(0, 80));

const pngB64 = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==';
const dataUrl = `data:image/png;base64,${pngB64}`;
const put = await fetch(`${API}/api/data/Item/${gadget.MetaId}`, {
  method: 'PUT',
  headers: { ...h, 'Content-Type': 'application/json' },
  body: JSON.stringify({ ...gadget, Image: dataUrl, Name: gadget.Name }),
});
const body = await put.text();
console.log('PUT dataUrl', put.status, body.slice(0, 500));

const put2 = await fetch(`${API}/api/data/Item/${gadget.MetaId}`, {
  method: 'PUT',
  headers: { ...h, 'Content-Type': 'application/json' },
  body: JSON.stringify({ Name: gadget.Name, Image: dataUrl }),
});
console.log('PUT minimal', put2.status, (await put2.text()).slice(0, 500));
