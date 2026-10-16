import dayjs from 'dayjs';
import 'dayjs/locale/vi';
import utc from 'dayjs/plugin/utc';
import timezone from 'dayjs/plugin/timezone';
import customParseFormat from 'dayjs/plugin/customParseFormat';

dayjs.extend(utc);
dayjs.extend(timezone);
dayjs.extend(customParseFormat);
dayjs.locale('vi');

/** All business dates are shown in Vietnam time regardless of the browser's zone (spec §0.4). */
export const APP_TIME_ZONE = 'Asia/Ho_Chi_Minh';
dayjs.tz.setDefault(APP_TIME_ZONE);

export default dayjs;
