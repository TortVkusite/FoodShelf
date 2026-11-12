import {chromium,expect} from '../frontend/node_modules/@playwright/test/index.mjs';
const url=process.env.E2E_BASE_URL||'http://localhost:5088';
const browser=await chromium.launch(process.env.PLAYWRIGHT_EXECUTABLE_PATH?{headless:true,executablePath:process.env.PLAYWRIGHT_EXECUTABLE_PATH}:{headless:true});
const page=await browser.newPage({viewport:{width:1280,height:720}});const errors=[];page.on('pageerror',e=>errors.push(e.message));
try{
 await page.goto(url);await expect(page.locator('app-root')).toHaveAttribute('ng-version',/^22\./);await expect(page.locator('.loading')).toHaveCount(0);
 await page.locator('nav button').filter({hasText:'Заказы'}).click();await expect(page.locator('.loading')).toHaveCount(0);await expect(page.locator('.orders tbody tr')).toHaveCount(6);
 await page.locator('.orders tr.archived').click();await expect(page.getByRole('dialog')).toHaveCount(0);
 await page.locator('.orders th button').filter({hasText:'Order ID'}).click();await expect(page.locator('.orders tbody tr').first()).toContainText('1001');
 await page.getByLabel('Email',{exact:true}).check();await expect(page.locator('.orders th').filter({hasText:'Email'})).toHaveCount(1);
 await page.locator('#add').click();await page.locator('[name=first_name]').fill('Браузер');await page.locator('[name=last_name]').fill('Проверка');await page.locator('[name=email]').fill('ui@example.com');await page.locator('[name=address]').fill('Брянск');await page.locator('#save').click();await expect(page.getByRole('dialog')).toHaveCount(0);await expect(page.getByText('7 total',{exact:true})).toBeVisible();
 await page.locator('.orders tbody tr').filter({hasText:'Браузер Проверка'}).getByRole('button',{name:/Удалить/}).click();await page.locator('#confirm-delete').click();await expect(page.getByRole('dialog')).toHaveCount(0);await expect(page.getByText('6 total',{exact:true})).toBeVisible();
 await page.setViewportSize({width:390,height:844});await page.locator('nav button').filter({hasText:'Мои продукты'}).click();await expect(page.locator('.loading')).toHaveCount(0);expect(await page.evaluate(()=>document.documentElement.scrollWidth)).toBe(390);
 expect(errors).toEqual([]);console.log('OK: Angular, AJAX, order sorting, Archive protection, optional columns, create/delete form, mobile width, no JavaScript errors.');
}finally{await browser.close()}
