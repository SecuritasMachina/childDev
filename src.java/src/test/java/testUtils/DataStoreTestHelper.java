package testUtils;

import java.io.Closeable;

import com.ackdev.childDev.jdo.GoalItem;
import com.ackdev.childDev.jdo.GoalItemProgress;
import com.ackdev.childDev.jdo.JournalItem;
import com.ackdev.jdo.common.FeedbackItem;
import com.ackdev.jdo.common.SystemHistory;
import com.ackdev.jdo.security.Address;
import com.ackdev.jdo.security.ContactInfo;
import com.ackdev.jdo.security.ContactMethods;
import com.ackdev.jdo.security.ContactsJDO;
import com.ackdev.jdo.security.History;
import com.common.security.jdo.AuthHashList;
import com.common.security.jdo.UserAccountV2;
import com.google.appengine.tools.development.testing.LocalDatastoreServiceTestConfig;
import com.google.appengine.tools.development.testing.LocalServiceTestHelper;
import com.googlecode.objectify.ObjectifyService;

public class DataStoreTestHelper {
	private final static LocalServiceTestHelper helper = new LocalServiceTestHelper(new LocalDatastoreServiceTestConfig());
	private static Closeable session;
	public static void closeObjectify() {
		helper.tearDown();
	}
	public static void setupObjectify() {
		//LocalOfyService.ofy();
		helper.setUp();
		ObjectifyService.init();
		session = ObjectifyService.begin();


		ObjectifyService.factory().register(GoalItemProgress.class);
		ObjectifyService.factory().register(UserAccountV2.class);
		ObjectifyService.factory().register(AuthHashList.class);

		ObjectifyService.factory().register(FeedbackItem.class);
		ObjectifyService.factory().register(ContactInfo.class);
		ObjectifyService.factory().register(ContactsJDO.class);
		ObjectifyService.factory().register(History.class);
		ObjectifyService.factory().register(JournalItem.class);
		ObjectifyService.factory().register(GoalItem.class);
        // Jobs
		ObjectifyService.factory().register(Address.class);
		ObjectifyService.factory().register(ContactMethods.class);
		ObjectifyService.factory().register(SystemHistory.class);

	}
}
