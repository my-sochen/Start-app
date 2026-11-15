/* Offline local metadata-preparation checks. Does not prove publication or indexing. */
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const site=new URL('../',import.meta.url),origin='https://my-sochen.github.io/Start-app';
const langs=['he','en','ru','zh-Hans','ja','fr','es','de'];
const url=(l,p)=>origin+(l==='he'&&p==='index'?'/':'/'+(l==='he'?'':l+'/')+p+'.html');
let count=0;const ok=(yes,msg)=>{assert.ok(yes,msg);count++};
const sitemap=readFileSync(new URL('sitemap.xml',site),'utf8');
const locs=[...sitemap.matchAll(/<loc>([^<]+)<\/loc>/g)].map(m=>m[1]);
ok(locs.length===16&&new Set(locs).size===16,'16 unique sitemap entries');
for(const l of langs)for(const p of ['index','docs']){
 const html=readFileSync(new URL((l==='he'?'':l+'/')+p+'.html',site),'utf8');
 const canonical=html.match(/<link\b(?=[^>]*rel="canonical")(?=[^>]*href="([^"]+)")[^>]*>/);
 ok(canonical&&canonical[1]===url(l,p),l+' '+p+' self canonical');
 ok(locs.includes(url(l,p)),l+' '+p+' in sitemap');
 const alternates=[...html.matchAll(/<link\b(?=[^>]*rel="alternate")(?=[^>]*hreflang="([^"]+)")(?=[^>]*href="([^"]+)")[^>]*>/g)].map(m=>[m[1],m[2]]);
 ok(alternates.length===9,l+' '+p+' 8 reciprocal language links + default');
 for(const a of langs.concat('x-default'))ok(alternates.some(([lang,href])=>lang===a&&href===url(a==='x-default'?'he':a,p)),l+' '+p+' alternate '+a);
 ok(!/noindex|nofollow/i.test(html),l+' '+p+' no accidental indexing exclusion');
 const item=sitemap.split('<url>').find(x=>x.includes('<loc>'+url(l,p)+'</loc>'));
 for(const a of langs.concat('x-default'))ok(item&&item.includes('hreflang="'+a+'" href="'+url(a==='x-default'?'he':a,p)+'"'),l+' '+p+' sitemap alternate '+a);
}
const robots=readFileSync(new URL('robots.txt',site),'utf8');ok(robots.includes('Sitemap: '+origin+'/sitemap.xml')&&!/Disallow: \//.test(robots),'robots unchanged and matches sitemap');
console.log(count+' local multilingual SEO checks passed');
