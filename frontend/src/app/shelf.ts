import { Component, ChangeDetectorRef, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, NgForm } from '@angular/forms';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

type View = 'products' | 'categories' | 'locations' | 'orders' | 'customers' | 'reports';
interface Reference {
  id: number;
  name: string;
}
interface Page<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}
interface Product extends Reference {
  quantity: number;
  unit: string;
  expirationDate: string;
  categoryId: number;
  category: string;
  storageLocationId: number;
  storageLocation: string;
  daysLeft: number;
  status: string;
}
interface Customer {
  id: number;
  firstName: string;
  lastName: string;
  birthDate: string;
  email: string;
  address: string;
}
interface OrderRow {
  id: number;
  first_name: string;
  last_name: string;
  email: string;
  address: string;
  data: string;
  amount: number;
  status: string;
  customerId?: number;
}

@Component({
  selector: 'app-root',
  imports: [CommonModule, FormsModule],
  templateUrl: './shelf.html',
})
export class App implements OnInit {
  private http = inject(HttpClient);
  private cd = inject(ChangeDetectorRef);
  view: View = 'products';
  loading = false;
  saving = false;
  error = '';
  message = '';
  today = '';
  demo = false;
  products: Product[] = [];
  categories: Reference[] = [];
  locations: Reference[] = [];
  customers: Customer[] = [];
  orders: OrderRow[] = [];
  stats: any = { total: 0, expired: 0, soon: 0, fresh: 0, categories: [] };
  birthdays: any[] = [];
  hourly: any[] = [];
  inventory: any[] = [];
  search = '';
  status = '';
  category = '';
  location = '';
  unit = '';
  expirationFrom = '';
  expirationTo = '';
  quantityMin = '';
  quantityMax = '';
  showFilters = false;
  mockMode = true;
  showEmail = false;
  showAddress = false;
  page = 1;
  pageSize = 10;
  sortKey = 'expirationDate';
  sortDirection = 1;
  modal = false;
  deleteItem: any = null;
  form: any = {};
  formError = '';
  private revision = 0;
  private timer?: ReturnType<typeof setTimeout>;
  readonly nav: { id: View; title: string; icon: string }[] = [
    { id: 'products', title: 'Мои продукты', icon: '▦' },
    { id: 'categories', title: 'Категории', icon: '◉' },
    { id: 'locations', title: 'Места хранения', icon: '▤' },
    { id: 'customers', title: 'Клиенты', icon: '♙' },
    { id: 'orders', title: 'Заказы', icon: '≡' },
    { id: 'reports', title: 'Отчёты', icon: '↗' },
  ];
  get title() {
    return this.nav.find((x) => x.id === this.view)?.title || '';
  }
  get subtitle() {
    return {
      products: 'Всё, что хранится дома, — в одном месте.',
      categories: 'Группируйте продукты, чтобы проще находить нужное.',
      locations: 'У каждого запаса своё место.',
      customers: 'Клиенты и контактные данные.',
      orders: 'Таблица заказов и управление записями.',
      reports: 'Результаты SQL-отчётов.',
    }[this.view];
  }
  get buttonText() {
    return {
      products: 'Добавить продукт',
      categories: 'Добавить категорию',
      locations: 'Добавить место',
      customers: 'Добавить клиента',
      orders: 'New Entry',
      reports: '',
    }[this.view];
  }
  async ngOnInit() {
    await this.run(async () => {
      const info = await this.get<any>('/api/info');
      this.today = info.today;
      this.demo = info.demo;
      await this.refresh();
    });
  }
  private get<T>(url: string) {
    return firstValueFrom(this.http.get<T>(url));
  }
  private async all<T>(url: string): Promise<T[]> {
    let items: T[] = [];
    for (let page = 1; ; page++) {
      const r = await this.get<Page<T>>(
        `${url}${url.includes('?') ? '&' : '?'}page=${page}&pageSize=100`,
      );
      items.push(...r.items);
      if (items.length >= r.total) return items;
    }
  }
  private async run(fn: () => Promise<void>) {
    this.loading = true;
    this.error = '';
    this.cd.markForCheck();
    try {
      await fn();
    } catch (e) {
      this.error = this.errorText(e);
    } finally {
      this.loading = false;
      this.cd.markForCheck();
    }
  }
  private errorText(e: unknown) {
    return e instanceof HttpErrorResponse
      ? e.error?.error || e.error?.detail || `Ошибка запроса (${e.status}). Проверьте подключение.`
      : e instanceof Error
        ? e.message
        : 'Не удалось выполнить операцию.';
  }
  async navigate(view: View) {
    this.view = view;
    this.page = 1;
    this.search = '';
    this.status = '';
    this.sortKey = view === 'products' ? 'expirationDate' : view === 'orders' ? 'data' : 'name';
    this.sortDirection = view === 'orders' ? -1 : 1;
    await this.run(() => this.refresh());
  }
  async refresh() {
    [this.categories, this.locations] = await Promise.all([
      this.get<Reference[]>('/api/categories'),
      this.get<Reference[]>('/api/locations'),
    ]);
    if (this.view === 'products') {
      await this.loadProducts();
      this.stats = await this.get('/api/statistics');
    }
    if (this.view === 'customers') this.customers = await this.all<Customer>('/api/customers');
    if (this.view === 'orders') {
      this.customers = await this.all<Customer>('/api/customers');
      if (this.mockMode) {
        const initial = await this.get<OrderRow[]>('/MOCK_DATA.json');
        const saved = localStorage.getItem('foodshelf-mock-orders-v1');
        try {
          this.orders = saved ? JSON.parse(saved) : initial;
        } catch {
          this.orders = initial;
        }
      } else {
        const items = await this.all<any>('/api/orders');
        this.orders = items.map((x) => ({
          id: x.id,
          first_name: x.firstName,
          last_name: x.lastName,
          email: x.email,
          address: x.address,
          data: x.orderedAt,
          amount: x.amount,
          status: x.status,
          customerId: x.customerId,
        }));
      }
    }
    if (this.view === 'reports')
      [this.birthdays, this.hourly, this.inventory] = await Promise.all([
        this.get<any[]>('/api/reports/birthday-orders'),
        this.get<any[]>('/api/reports/hourly-orders'),
        this.get<any[]>('/api/reports/inventory'),
      ]);
    this.page = Math.min(this.page, Math.max(1, Math.ceil(this.filtered.length / this.pageSize)));
  }
  private async loadProducts() {
    const rev = ++this.revision;
    const q = new URLSearchParams();
    for (const [k, v] of Object.entries({
      search: this.search,
      status: this.status,
      categoryId: this.category,
      storageLocationId: this.location,
      unit: this.unit,
      expirationFrom: this.expirationFrom,
      expirationTo: this.expirationTo,
      quantityMin: this.quantityMin,
      quantityMax: this.quantityMax,
    }))
      if (v !== '') q.set(k, v);
    const items = await this.all<Product>('/api/products?' + q);
    if (rev === this.revision) this.products = items;
  }
  filterProducts() {
    this.page = 1;
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.run(() => this.loadProducts()), 250);
  }
  filterStatus(status: string) {
    this.status = status;
    this.page = 1;
    void this.run(() => this.loadProducts());
  }
  async changeSource() {
    this.page = 1;
    await this.run(() => this.refresh());
  }
  get filtered(): any[] {
    let items: any[] =
      this.view === 'products'
        ? this.products
        : this.view === 'categories'
          ? this.categories
          : this.view === 'locations'
            ? this.locations
            : this.view === 'customers'
              ? this.customers
              : this.view === 'orders'
                ? this.orders
                : [];
    if (this.view !== 'products' && this.search.trim()) {
      const key = this.search.trim().toLocaleLowerCase('ru');
      items = items.filter((x) => Object.values(x).join(' ').toLocaleLowerCase('ru').includes(key));
    }
    return [...items].sort((a, b) => {
      const av = this.sortValue(a),
        bv = this.sortValue(b);
      return (
        this.sortDirection *
          (typeof av === 'number' && typeof bv === 'number'
            ? av - bv
            : String(av).localeCompare(String(bv), 'ru', { numeric: true })) || a.id - b.id
      );
    });
  }
  private sortValue(x: any): string | number {
    if (this.sortKey === 'customer') return `${x.first_name} ${x.last_name}`;
    return x[this.sortKey] ?? x.name ?? `${x.firstName} ${x.lastName}`;
  }
  get visible() {
    return this.filtered.slice((this.page - 1) * this.pageSize, this.page * this.pageSize);
  }
  get totalPages() {
    return Math.max(1, Math.ceil(this.filtered.length / this.pageSize));
  }
  sort(key: string) {
    this.sortDirection = this.sortKey === key ? -this.sortDirection : 1;
    this.sortKey = key;
    this.page = 1;
  }
  arrow(key: string) {
    return this.sortKey === key ? (this.sortDirection === 1 ? ' ↑' : ' ↓') : '';
  }
  statusLabel(s: string) {
    return (
      (
        {
          expired: 'Просрочено',
          soon: 'Скоро истечёт',
          fresh: 'Всё в порядке',
          Pending: 'Не обработан',
          Cancelled: 'Отменён',
          Completed: 'Выполнен',
          Archive: 'Архив',
          Delivered: 'Доставлен',
          Refund: 'Возврат',
        } as Record<string, string>
      )[s] || s
    );
  }
  open(item?: any) {
    if (this.view === 'orders' && item?.status === 'Archive') return;
    if (this.view === 'products' && (!this.categories.length || !this.locations.length)) {
      this.error = 'Сначала добавьте категорию и место хранения.';
      return;
    }
    if (this.view === 'orders' && !this.mockMode && !this.customers.length) {
      this.error = 'Сначала добавьте клиента.';
      return;
    }
    this.formError = '';
    this.form = {
      name: '',
      quantity: 1,
      unit: 'шт',
      expirationDate: this.today,
      categoryId: this.categories[0]?.id,
      storageLocationId: this.locations[0]?.id,
      firstName: '',
      lastName: '',
      birthDate: '2000-01-01',
      email: '',
      address: '',
      first_name: '',
      last_name: '',
      data: this.today + 'T12:00',
      amount: 100,
      status: 'Pending',
      customerId: this.customers[0]?.id,
      ...item,
    };
    if (this.view === 'orders' && item?.data) this.form.data = this.toLocalInput(item.data);
    this.modal = true;
  }
  private toLocalInput(value: string) {
    const d = new Date(value);
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}T${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
  }
  async save(ngForm: NgForm) {
    if (ngForm.invalid) {
      this.formError = 'Заполните обязательные поля и проверьте формат данных.';
      return;
    }
    if (this.saving) return;
    this.saving = true;
    this.formError = '';
    try {
      const f = this.form;
      if (this.view === 'orders' && this.mockMode) {
        if (
          !Number.isFinite(f.amount) ||
          f.amount <= 0 ||
          Math.abs(Math.round(f.amount * 100) - f.amount * 100) > 0.00001
        )
          throw new Error('Сумма должна быть положительной с точностью до копеек.');
        const row: OrderRow = {
          id: f.id || Math.max(0, ...this.orders.map((o) => o.id)) + 1,
          first_name: f.first_name.trim(),
          last_name: f.last_name.trim(),
          email: f.email.trim(),
          address: f.address.trim(),
          data: new Date(f.data).toISOString(),
          amount: Number(f.amount),
          status: f.status,
        };
        if (f.id) this.orders = this.orders.map((o) => (o.id === f.id ? row : o));
        else this.orders.push(row);
        localStorage.setItem('foodshelf-mock-orders-v1', JSON.stringify(this.orders));
      } else {
        let body: any;
        if (this.view === 'products')
          body = {
            name: f.name,
            quantity: Number(f.quantity),
            unit: f.unit,
            expirationDate: f.expirationDate,
            categoryId: Number(f.categoryId),
            storageLocationId: Number(f.storageLocationId),
          };
        else if (this.view === 'customers')
          body = {
            firstName: f.firstName,
            lastName: f.lastName,
            birthDate: f.birthDate,
            email: f.email,
            address: f.address,
          };
        else if (this.view === 'orders')
          body = {
            amount: Number(f.amount),
            orderedAt: new Date(f.data).toISOString(),
            status: f.status,
            customerId: Number(f.customerId),
          };
        else body = { name: f.name };
        await firstValueFrom(
          this.http.request(f.id ? 'PUT' : 'POST', `/api/${this.view}${f.id ? '/' + f.id : ''}`, {
            body,
          }),
        );
      }
      this.modal = false;
      await this.refresh();
      this.notify('Запись сохранена');
    } catch (e) {
      this.formError = this.errorText(e);
    } finally {
      this.saving = false;
      this.cd.markForCheck();
    }
  }
  async remove() {
    if (this.saving || !this.deleteItem) return;
    this.saving = true;
    try {
      if (this.view === 'orders' && this.mockMode) {
        this.orders = this.orders.filter((x) => x.id !== this.deleteItem.id);
        localStorage.setItem('foodshelf-mock-orders-v1', JSON.stringify(this.orders));
      } else await firstValueFrom(this.http.delete(`/api/${this.view}/${this.deleteItem.id}`));
      this.deleteItem = null;
      await this.refresh();
      this.notify('Запись удалена');
    } catch (e) {
      this.error = this.errorText(e);
      this.deleteItem = null;
    } finally {
      this.saving = false;
      this.cd.markForCheck();
    }
  }
  notify(text: string) {
    this.message = text;
    setTimeout(() => {
      this.message = '';
      this.cd.markForCheck();
    }, 3500);
  }
  resetMock() {
    if (!confirm('Сбросить изменения демонстрационных заказов?')) return;
    localStorage.removeItem('foodshelf-mock-orders-v1');
    void this.run(() => this.refresh());
  }
}
